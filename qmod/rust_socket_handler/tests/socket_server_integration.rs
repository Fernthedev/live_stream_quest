use std::io;
use std::net::{SocketAddr, TcpListener, UdpSocket};
use std::sync::Arc;
use std::time::Duration;

use live_stream_quest_rust::codec;
use live_stream_quest_rust::socket_handler::SocketTransport;
use live_stream_quest_rust::RustSocketServer;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::{TcpStream, UdpSocket as TokioUdpSocket};
use tokio::sync::mpsc;

fn reserve_tcp_port() -> u16 {
    TcpListener::bind(("127.0.0.1", 0))
        .unwrap()
        .local_addr()
        .unwrap()
        .port()
}

fn reserve_udp_port() -> u16 {
    UdpSocket::bind(("127.0.0.1", 0))
        .unwrap()
        .local_addr()
        .unwrap()
        .port()
}

async fn wait_for_packet(
    receiver: &mut mpsc::UnboundedReceiver<Vec<u8>>,
) -> Vec<u8> {
    tokio::time::timeout(Duration::from_secs(2), receiver.recv())
        .await
        .expect("timed out waiting for packet")
        .expect("server closed packet channel unexpectedly")
}

#[tokio::test]
async fn tcp_server_receives_packets_and_writes_responses() {
    let port = reserve_tcp_port();
    let (packet_tx, mut packet_rx) = mpsc::unbounded_channel::<Vec<u8>>();
    let server = Arc::new(
        RustSocketServer::new_with_transport(
            port,
            SocketTransport::TCP,
            move |packet| {
                packet_tx.send(packet.to_vec()).unwrap();
            },
        )
        .await
        .unwrap(),
    );

    let listen_server = Arc::clone(&server);
    let listen_task = tokio::spawn(async move { listen_server.listen().await });

    let addr = SocketAddr::from(([127, 0, 0, 1], port));
    let client_task = tokio::spawn(async move {
        let mut stream = TcpStream::connect(addr).await?;
        stream.write_all(&codec::encode_frame(b"tcp-request")).await?;

        let mut size_buf = [0u8; std::mem::size_of::<u32>()];
        stream.read_exact(&mut size_buf).await?;
        let packet_len = codec::decode_packet_size(size_buf);

        let mut payload = vec![0u8; packet_len];
        stream.read_exact(&mut payload).await?;
        assert_eq!(payload, b"tcp-response");
        Ok::<(), io::Error>(())
    });

    let received = wait_for_packet(&mut packet_rx).await;
    assert_eq!(received, b"tcp-request");
    assert!(server.has_connection().await);

    server.send_packet(b"tcp-response").await.unwrap();
    tokio::time::timeout(Duration::from_secs(2), client_task)
        .await
        .expect("timed out waiting for TCP client")
        .expect("TCP client task failed")
        .unwrap();

    listen_task.abort();
}

#[tokio::test]
async fn udp_server_receives_packets_and_writes_responses() {
    let port = reserve_udp_port();
    let (packet_tx, mut packet_rx) = mpsc::unbounded_channel::<Vec<u8>>();
    let server = Arc::new(
        RustSocketServer::new_with_transport(
            port,
            SocketTransport::UDP,
            move |packet| {
                packet_tx.send(packet.to_vec()).unwrap();
            },
        )
        .await
        .unwrap(),
    );

    let listen_server = Arc::clone(&server);
    let listen_task = tokio::spawn(async move { listen_server.listen().await });

    let addr = SocketAddr::from(([127, 0, 0, 1], port));
    let client_task = tokio::spawn(async move {
        let client = TokioUdpSocket::bind(("127.0.0.1", 0)).await?;
        client.send_to(&codec::encode_frame(b"udp-request"), addr).await?;

        let mut buffer = vec![0u8; 64];
        let (len, _) = client.recv_from(&mut buffer).await?;
        let frame = &buffer[..len];
        let payload = codec::decode_frame(frame).expect("server response should be framed");
        assert_eq!(payload, b"udp-response");
        Ok::<(), io::Error>(())
    });

    let received = wait_for_packet(&mut packet_rx).await;
    assert_eq!(received, b"udp-request");
    assert!(server.has_connection().await);

    server.send_packet(b"udp-response").await.unwrap();
    tokio::time::timeout(Duration::from_secs(2), client_task)
        .await
        .expect("timed out waiting for UDP client")
        .expect("UDP client task failed")
        .unwrap();

    listen_task.abort();
}