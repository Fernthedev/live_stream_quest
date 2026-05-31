use std::collections::{HashMap, HashSet};
use std::io;
use std::net::SocketAddr;
use std::sync::Arc;

use tokio::net::{TcpListener, UdpSocket};
use tokio::sync::Mutex;

use crate::socket_handler::tcp::TcpBackend;
use crate::socket_handler::udp::UdpBackend;

mod tcp;
mod udp;


type PacketCallback = Arc<dyn Fn(&[u8]) + Send + Sync + 'static>;


// The TransportBackend enum abstracts over the TCP and UDP implementations, allowing the RustSocketServer to provide a unified API regardless of the underlying transport protocol.
enum TransportBackend {
    Tcp(TcpBackend),
    Udp(UdpBackend),
}

pub struct RustSocketServer {
    backend: TransportBackend,
    on_packet: PacketCallback,
}

#[repr(C)]
#[derive(Copy, Clone, Debug, Eq, PartialEq)]
pub enum SocketTransport {
    TCP = 0,
    UDP = 1,
}

impl RustSocketServer {
    pub async fn new(
        port: u16,
        on_packet: impl Fn(&[u8]) + Send + Sync + 'static,
    ) -> io::Result<Self> {
        Self::new_with_transport(port, SocketTransport::TCP, on_packet).await
    }

    pub async fn new_with_transport(
        port: u16,
        transport: SocketTransport,
        on_packet: impl Fn(&[u8]) + Send + Sync + 'static,
    ) -> io::Result<Self> {
        let bind_addr = SocketAddr::from(([0, 0, 0, 0], port));
        let backend = match transport {
            SocketTransport::TCP => TransportBackend::Tcp(TcpBackend {
                listener: TcpListener::bind(bind_addr).await?,
                clients: Arc::new(Mutex::new(HashMap::new())),
            }),
            SocketTransport::UDP => TransportBackend::Udp(UdpBackend {
                server_socket: UdpSocket::bind(bind_addr).await?,
                peers: Arc::new(Mutex::new(HashSet::new())),
            }),
        };

        Ok(Self {
            backend,
            on_packet: Arc::new(on_packet),
        })
    }

    pub async fn listen(&self) -> io::Result<()> {
        self.backend.listen(Arc::clone(&self.on_packet)).await
    }

    pub async fn has_connection(&self) -> bool {
        self.backend.has_connection().await
    }

    pub fn schedule_async<F>(&self, f: F)
    where
        F: FnOnce() + Send + 'static,
    {
        tokio::spawn(async move {
            f();
        });
    }

    pub async fn send_packet(&self, payload: &[u8]) -> io::Result<()> {
        self.backend.send_packet(payload).await
    }
}

impl TransportBackend {
    async fn listen(&self, on_packet: PacketCallback) -> io::Result<()> {
        match self {
            TransportBackend::Tcp(tcp) => tcp.listen(on_packet).await,
            TransportBackend::Udp(udp) => udp.listen(on_packet).await,
        }
    }

    async fn has_connection(&self) -> bool {
        match self {
            TransportBackend::Tcp(tcp) => !tcp.clients.lock().await.is_empty(),
            TransportBackend::Udp(udp) => !udp.peers.lock().await.is_empty(),
        }
    }

    async fn send_packet(&self, payload: &[u8]) -> io::Result<()> {
        match self {
            TransportBackend::Tcp(tcp) => tcp.send_packet(payload).await,
            TransportBackend::Udp(udp) => udp.send_packet(payload).await,
        }
    }
}
