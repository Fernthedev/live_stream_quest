use crate::codec::{self, PacketSize};

use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    net::{TcpListener, TcpStream, tcp::{OwnedReadHalf, OwnedWriteHalf}},
    sync::Mutex,
};

use std::{collections::HashMap, io, sync::Arc};

use super::PacketCallback;
use std::net::SocketAddr;


type SharedClient = Arc<Mutex<OwnedWriteHalf>>;

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
        let (read_half, write_half) = socket.into_split();
        let client = Arc::new(Mutex::new(write_half));
        self.clients.lock().await.insert(peer, Arc::clone(&client));

        let clients = Arc::clone(&self.clients);
        tokio::spawn(async move {
            if let Err(err) = Self::read_client(read_half, on_packet).await {
                eprintln!("socket client {peer} disconnected: {err}");
            }
            clients.lock().await.remove(&peer);
        });
    }

    /// Reads framed packets from a TCP client connection and dispatches each packet to the callback.
    pub(crate) async fn read_client(read_half: OwnedReadHalf, on_packet: PacketCallback) -> io::Result<()> {
        let mut size_buf = [0u8; std::mem::size_of::<PacketSize>()];
        let mut read_half = read_half;

        loop {
            read_half.read_exact(&mut size_buf).await?;
            let packet_len = codec::decode_packet_size(size_buf);

            let mut packet = vec![0u8; packet_len];
            read_half.read_exact(&mut packet).await?;

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
    use std::collections::HashMap;
    use std::net::SocketAddr;
    use std::sync::Arc;

    use tokio::net::{TcpListener, TcpStream};
    use tokio::sync::{mpsc, Mutex};

    use super::*;

    use crate::codec;

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
        let (read_half, _write_half) = server_stream.into_split();

        let (tx, mut rx) = mpsc::unbounded_channel::<Vec<u8>>();
        let callback: PacketCallback = Arc::new(move |packet: &[u8]| {
            tx.send(packet.to_vec()).unwrap();
        });

        let reader = tokio::spawn(TcpBackend::read_client(read_half, callback));

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
        let (_read_half, write_half) = server_stream.into_split();
        let clients = Arc::new(Mutex::new(HashMap::from([(
            peer,
            Arc::new(Mutex::new(write_half)),
        )])));

        let backend = TcpBackend { listener, clients };

        backend.send_packet(b"send-me").await.unwrap();
        client_task.await.unwrap();
    }
}
