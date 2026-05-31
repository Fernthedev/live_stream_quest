pub mod socket_handler;

pub mod bindings;

pub use socket_handler::RustSocketServer;

#[global_allocator]
static GLOBAL: mimalloc::MiMalloc = mimalloc::MiMalloc;
