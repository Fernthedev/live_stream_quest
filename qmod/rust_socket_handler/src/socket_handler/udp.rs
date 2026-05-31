use std::{collections::HashSet, io, sync::Arc};

use super::PacketCallback;

use tokio::{net::{TcpStream, UdpSocket}, sync::Mutex};

use std::net::SocketAddr;

pub struct UdpBackend {
    pub socket: UdpSocket,
    pub peers: Arc<Mutex<HashSet<SocketAddr>>>,
}

impl UdpBackend {
    pub(crate) async fn send_packet(&self, payload: &[u8]) -> io::Result<()> {
        let peers = self.peers.lock().await.clone();
        if peers.is_empty() {
            return Ok(());
        }

        let mut dead_peers = Vec::new();
        for peer in peers {
            if self.socket.send_to(payload, peer).await.is_err() {
                dead_peers.push(peer);
            }
        }

        if !dead_peers.is_empty() {
            let mut peers = self.peers.lock().await;
            for peer in dead_peers {
                peers.remove(&peer);
            }
        }

        Ok(())
    }

    /// For UDP backends, this is a no-op since there is no connection to manage, but we still want to preserve the API for compatibility with TCP backends.
    pub(crate) async fn handle_connect(
        &self,
        _peer: SocketAddr,
        _socket: TcpStream,
        _on_packet: PacketCallback,
    ) {
    }

    /// Listens for incoming UDP packets, dispatching each received packet to the callback and tracking unique peer addresses.
    pub(crate) async fn listen(&self, on_packet: PacketCallback) -> io::Result<()> {
        let mut buffer = vec![0u8; 65_507];
        loop {
            let (len, peer) = self.socket.recv_from(&mut buffer).await?;
            self.peers.lock().await.insert(peer);

            let packet = buffer[..len].to_vec();
            let callback = Arc::clone(&on_packet);
            tokio::spawn(async move {
                callback(&packet);
            });
        }
    }
}
