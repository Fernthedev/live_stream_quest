use super::SharedClient;

use tokio::{io::{AsyncReadExt, AsyncWriteExt}, net::TcpListener, sync::Mutex};

use std::{collections::HashMap, io, sync::Arc};

use super::PacketCallback;

use tokio::net::TcpStream;

use std::net::SocketAddr;

use super::PacketSize;


pub struct TcpBackend {
    pub listener: TcpListener,
    pub clients: Arc<Mutex<HashMap<SocketAddr, SharedClient>>>,
}

impl TcpBackend {
    pub(crate) async fn send_packet(&self, payload: &[u8]) -> io::Result<()> {
        let mut frame = Vec::with_capacity(payload.len() + std::mem::size_of::<PacketSize>());
        frame.extend_from_slice(&(payload.len() as PacketSize).to_be_bytes());
        frame.extend_from_slice(payload);

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

    pub(crate) async fn handle_connect(&self, peer: SocketAddr, socket: TcpStream, on_packet: PacketCallback) {
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
    pub(crate) async fn read_client(client: SharedClient, on_packet: PacketCallback) -> io::Result<()> {
        let mut size_buf = [0u8; std::mem::size_of::<PacketSize>()];

        loop {
            let mut socket_locked = client.lock().await;
            socket_locked.read_exact(&mut size_buf).await?;
            let packet_len = PacketSize::from_be_bytes(size_buf) as usize;

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
