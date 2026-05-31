pub type PacketSize = u32;

pub fn encode_frame(payload: &[u8]) -> Vec<u8> {
    let mut frame = Vec::with_capacity(payload.len() + std::mem::size_of::<PacketSize>());
    frame.extend_from_slice(&(payload.len() as PacketSize).to_be_bytes());
    frame.extend_from_slice(payload);
    frame
}

pub fn decode_packet_size(size_buf: [u8; std::mem::size_of::<PacketSize>()]) -> usize {
    PacketSize::from_be_bytes(size_buf) as usize
}

pub fn decode_frame(frame: &[u8]) -> Option<&[u8]> {
    let prefix_len = std::mem::size_of::<PacketSize>();
    if frame.len() < prefix_len {
        return None;
    }

    let mut size_buf = [0u8; std::mem::size_of::<PacketSize>()];
    size_buf.copy_from_slice(&frame[..prefix_len]);
    let packet_len = decode_packet_size(size_buf);

    if frame.len() != prefix_len + packet_len {
        return None;
    }

    Some(&frame[prefix_len..])
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn encode_frame_prefixes_payload_length() {
        let frame = encode_frame(b"hello");

        assert_eq!(frame.len(), 4 + 5);
        assert_eq!(&frame[..4], &(5u32).to_be_bytes());
        assert_eq!(&frame[4..], b"hello");
    }

    #[test]
    fn decode_packet_size_uses_big_endian_prefix() {
        let size = decode_packet_size((13u32).to_be_bytes());

        assert_eq!(size, 13);
    }

    #[test]
    fn decode_frame_returns_payload_when_length_matches() {
        let frame = encode_frame(b"udp-payload");

        assert_eq!(decode_frame(&frame), Some(&b"udp-payload"[..]));
    }

    #[test]
    fn decode_frame_rejects_truncated_or_mismatched_frames() {
        assert_eq!(decode_frame(&[]), None);

        let mut frame = encode_frame(b"hello");
        frame.pop();
        assert_eq!(decode_frame(&frame), None);
    }

}
