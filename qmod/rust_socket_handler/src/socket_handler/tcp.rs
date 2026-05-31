use crate::codec::{PacketSize, self};

use super::SharedClient;

use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    net::TcpListener,
    sync::Mutex,
};

use std::{collections::HashMap, io, sync::Arc};

use super::PacketCallback;

use tokio::net::TcpStream;

use std::net::SocketAddr;

pub struct TcpBackend {
    pub listener: TcpListener,
    pub clients: Arc<Mutex<HashMap<SocketAddr, SharedClient>>>,
}

impl TcpBackend {
    pub(crate) async fn send_packet(&self, payload: &[u8]) -> io::Result<()> {
        let frame = codec::encode_frame(payload);

        let clients = self.clients.lock().await.clone();
        let mut dead_clients = Vec::new();

        for (peer, client) in clients {
            let mut socket = client.lock().await;
            if socket.write_all(&frame).await.is_err() {
                dead_clients.push(peer);
            }
        }

        if !dead_clients.is_empty() {
            let mut clients = self.clients.lock().await;
            for peer in dead_clients {
                clients.remove(&peer);
            }
        }

        Ok(())
    }

    pub(crate) async fn handle_connect(
        &self,
        peer: SocketAddr,
        socket: TcpStream,
        on_packet: PacketCallback,
    ) {
        let client = Arc::new(Mutex::new(socket));
        self.clients.lock().await.insert(peer, Arc::clone(&client));

        let clients = Arc::clone(&self.clients);
        tokio::spawn(async move {
            if let Err(err) = Self::read_client(client, on_packet).await {
                eprintln!("socket client {peer} disconnected: {err}");
            }
            clients.lock().await.remove(&peer);
        });
    }

    /// Reads framed packets from a TCP client connection and dispatches each packet to the callback.
    pub(crate) async fn read_client(
        client: SharedClient,
        on_packet: PacketCallback,
    ) -> io::Result<()> {
        let mut size_buf = [0u8; std::mem::size_of::<PacketSize>()];

        loop {
            let mut socket_locked = client.lock().await;
            socket_locked.read_exact(&mut size_buf).await?;
            let packet_len = codec::decode_packet_size(size_buf);

            let mut packet = vec![0u8; packet_len];
            socket_locked.read_exact(&mut packet).await?;
            drop(socket_locked);

            let callback = Arc::clone(&on_packet);
            tokio::spawn(async move {
                callback(&packet);
            });
        }
    }

    pub(crate) async fn listen(&self, on_packet: PacketCallback) -> io::Result<()> {
        loop {
            let (socket, peer) = self.listener.accept().await?;
            self.handle_connect(peer, socket, Arc::clone(&on_packet))
                .await;
        }
    }
}

#[cfg(test)]
mod tests {
    use tokio::sync::mpsc;

    use crate::codec;

    use super::*;

    #[tokio::test]
    async fn read_client_dispatches_payload_after_length_prefix() {
        let listener = TcpListener::bind(SocketAddr::from(([127, 0, 0, 1], 0)))
            .await
            .unwrap();
        let addr = listener.local_addr().unwrap();

        let client_task = tokio::spawn(async move {
            let mut stream = TcpStream::connect(addr).await.unwrap();
            let frame = codec::encode_frame(b"payload-bytes");
            stream.write_all(&frame).await.unwrap();
        });

        let (server_stream, _) = listener.accept().await.unwrap();
        let server_stream = Arc::new(Mutex::new(server_stream));

        let (tx, mut rx) = mpsc::unbounded_channel::<Vec<u8>>();
        let callback: PacketCallback = Arc::new(move |packet: &[u8]| {
            tx.send(packet.to_vec()).unwrap();
        });

        let reader = tokio::spawn(TcpBackend::read_client(
            Arc::clone(&server_stream),
            callback,
        ));

        let received = rx.recv().await.unwrap();
        assert_eq!(received, b"payload-bytes");

        client_task.await.unwrap();
        reader.abort();
    }

    #[tokio::test]
    async fn send_packet_writes_framed_payload_to_connected_client() {
        let listener = TcpListener::bind(SocketAddr::from(([127, 0, 0, 1], 0)))
            .await
            .unwrap();
        let addr = listener.local_addr().unwrap();

        let client_task = tokio::spawn(async move {
            let mut stream = TcpStream::connect(addr).await.unwrap();

            let mut size_buf = [0u8; 4];
            stream.read_exact(&mut size_buf).await.unwrap();
            let packet_len = codec::decode_packet_size(size_buf);

            let mut payload = vec![0u8; packet_len];
            stream.read_exact(&mut payload).await.unwrap();

            assert_eq!(payload, b"send-me");
        });

        let (server_stream, peer) = listener.accept().await.unwrap();
        let clients = Arc::new(Mutex::new(HashMap::from([(
            peer,
            Arc::new(Mutex::new(server_stream)),
        )])));

        let backend = TcpBackend { listener, clients };

        backend.send_packet(b"send-me").await.unwrap();
        client_task.await.unwrap();
    }
}
