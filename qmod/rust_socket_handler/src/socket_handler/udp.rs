use std::{collections::HashSet, io, sync::Arc};

use super::PacketCallback;
use crate::codec;

use tokio::{net::{TcpStream, UdpSocket}, sync::Mutex};

use std::net::SocketAddr;

pub struct UdpBackend {
    pub server_socket: UdpSocket,
    pub peers: Arc<Mutex<HashSet<SocketAddr>>>,
}

impl UdpBackend {
    pub(crate) async fn send_packet(&self, payload: &[u8]) -> io::Result<()> {
        let frame = codec::encode_frame(payload);
        let peers = self.peers.lock().await.clone();
        if peers.is_empty() {
            return Ok(());
        }

        let mut dead_peers = Vec::new();
        for peer in peers {
            if self.server_socket.send_to(&frame, peer).await.is_err() {
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

    /// For UDP backends, this is a no-op because the bound server socket is not connection-oriented.
    pub(crate) async fn handle_connect(
        &self,
        _peer: SocketAddr,
        _socket: TcpStream,
        _on_packet: PacketCallback,
    ) {
    }

    async fn handle_datagram(
        &self,
        peer: SocketAddr,
        datagram: &[u8],
        on_packet: &PacketCallback,
    ) {
        self.peers.lock().await.insert(peer);

        if let Some(packet) = codec::decode_frame(datagram) {
            let packet = packet.to_vec();
            let callback = Arc::clone(on_packet);
            tokio::spawn(async move {
                callback(&packet);
            });
        } else {
            // TODO: handle dropped / truncated UDP datagrams more explicitly per peer.
            // TODO: add packet ordering or sequence numbers if UDP reordering becomes an issue.
            // TODO: surface metrics for malformed framed datagrams.
        }
    }

    /// Listens for incoming UDP packets, dispatching each received packet to the callback and tracking unique peer addresses.
    pub(crate) async fn listen(&self, on_packet: PacketCallback) -> io::Result<()> {
        let mut buffer = vec![0u8; 65_507];
        loop {
            let (len, peer) = self.server_socket.recv_from(&mut buffer).await?;
            self.handle_datagram(peer, &buffer[..len], &on_packet).await;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    use std::collections::HashSet;
    use std::net::SocketAddr;

    use crate::codec;

    use tokio::net::UdpSocket;
    use tokio::sync::{mpsc, Mutex};

    #[tokio::test]
    async fn send_packet_writes_framed_datagram() {
        let receiver = UdpSocket::bind(SocketAddr::from(([127, 0, 0, 1], 0)))
            .await
            .unwrap();
        let receiver_addr = receiver.local_addr().unwrap();

        let sender = UdpSocket::bind(SocketAddr::from(([127, 0, 0, 1], 0)))
            .await
            .unwrap();

        let peers = Arc::new(Mutex::new(HashSet::from([receiver_addr])));
        let backend = UdpBackend { server_socket: sender, peers };

        let receive_task = tokio::spawn(async move {
            let mut buffer = vec![0u8; 64];
            let (len, _) = receiver.recv_from(&mut buffer).await.unwrap();
            let frame = &buffer[..len];

            assert_eq!(frame, codec::encode_frame(b"udp-send").as_slice());
        });

        backend.send_packet(b"udp-send").await.unwrap();
        receive_task.await.unwrap();
    }

    #[tokio::test]
    async fn listen_decodes_framed_datagram_payload() {
        let listener = UdpSocket::bind(SocketAddr::from(([127, 0, 0, 1], 0)))
            .await
            .unwrap();
        let addr = listener.local_addr().unwrap();

        let backend = UdpBackend {
            server_socket: listener,
            peers: Arc::new(Mutex::new(HashSet::new())),
        };

        let (tx, mut rx) = mpsc::unbounded_channel::<Vec<u8>>();
        let callback: PacketCallback = Arc::new(move |packet: &[u8]| {
            tx.send(packet.to_vec()).unwrap();
        });

        let listen_task = tokio::spawn(async move { backend.listen(callback).await });

        let sender = UdpSocket::bind(SocketAddr::from(([127, 0, 0, 1], 0)))
            .await
            .unwrap();
        let frame = codec::encode_frame(b"udp-payload");
        sender.send_to(&frame, addr).await.unwrap();

        let received = rx.recv().await.unwrap();
        assert_eq!(received, b"udp-payload");

        listen_task.abort();
    }

    #[tokio::test]
    async fn listen_tracks_multiple_udp_clients_independently() {
        let listener = UdpSocket::bind(SocketAddr::from(([127, 0, 0, 1], 0)))
            .await
            .unwrap();
        let addr = listener.local_addr().unwrap();

        let backend = UdpBackend {
            server_socket: listener,
            peers: Arc::new(Mutex::new(HashSet::new())),
        };

        let (tx, mut rx) = mpsc::unbounded_channel::<Vec<u8>>();
        let callback: PacketCallback = Arc::new(move |packet: &[u8]| {
            tx.send(packet.to_vec()).unwrap();
        });

        let listen_task = tokio::spawn(async move { backend.listen(callback).await });

        let sender_one = UdpSocket::bind(SocketAddr::from(([127, 0, 0, 1], 0)))
            .await
            .unwrap();
        let sender_two = UdpSocket::bind(SocketAddr::from(([127, 0, 0, 1], 0)))
            .await
            .unwrap();

        sender_one
            .send_to(&codec::encode_frame(b"client-one"), addr)
            .await
            .unwrap();
        sender_two
            .send_to(&codec::encode_frame(b"client-two"), addr)
            .await
            .unwrap();

        let mut received = vec![rx.recv().await.unwrap(), rx.recv().await.unwrap()];
        received.sort();
        assert_eq!(received, vec![b"client-one".to_vec(), b"client-two".to_vec()]);

        listen_task.abort();
    }

    #[tokio::test]
    async fn listen_ignores_unframed_datagram() {
        let listener = UdpSocket::bind(SocketAddr::from(([127, 0, 0, 1], 0)))
            .await
            .unwrap();
        let addr = listener.local_addr().unwrap();

        let backend = UdpBackend {
            server_socket: listener,
            peers: Arc::new(Mutex::new(HashSet::new())),
        };

        let (tx, mut rx) = mpsc::unbounded_channel::<Vec<u8>>();
        let callback: PacketCallback = Arc::new(move |packet: &[u8]| {
            tx.send(packet.to_vec()).unwrap();
        });

        let listen_task = tokio::spawn(async move { backend.listen(callback).await });

        let sender = UdpSocket::bind(SocketAddr::from(([127, 0, 0, 1], 0)))
            .await
            .unwrap();
        sender.send_to(b"raw-datagram", addr).await.unwrap();

        assert!(
            tokio::time::timeout(std::time::Duration::from_millis(100), rx.recv())
                .await
                .is_err(),
            "unframed datagrams should be ignored"
        );

        listen_task.abort();
    }

    #[tokio::test]
    async fn listen_ignores_truncated_frame_datagrams() {
        let listener = UdpSocket::bind(SocketAddr::from(([127, 0, 0, 1], 0)))
            .await
            .unwrap();
        let addr = listener.local_addr().unwrap();

        let backend = UdpBackend {
            server_socket: listener,
            peers: Arc::new(Mutex::new(HashSet::new())),
        };

        let (tx, mut rx) = mpsc::unbounded_channel::<Vec<u8>>();
        let callback: PacketCallback = Arc::new(move |packet: &[u8]| {
            tx.send(packet.to_vec()).unwrap();
        });

        let listen_task = tokio::spawn(async move { backend.listen(callback).await });

        let sender = UdpSocket::bind(SocketAddr::from(([127, 0, 0, 1], 0)))
            .await
            .unwrap();
        let mut frame = codec::encode_frame(b"udp-payload");
        frame.pop();
        sender.send_to(&frame, addr).await.unwrap();

        assert!(
            tokio::time::timeout(std::time::Duration::from_millis(100), rx.recv())
                .await
                .is_err(),
            "truncated UDP frames should be ignored"
        );

        listen_task.abort();
    }
}
