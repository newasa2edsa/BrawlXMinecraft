package dev.rehan.passthrough.client;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import dev.rehan.passthrough.MobWar;
import dev.rehan.passthrough.Nether;
import dev.rehan.passthrough.Passthrough;
import dev.rehan.passthrough.WorldBridge;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Base64;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.CopyOnWriteArrayList;
import net.minecraft.client.Minecraft;

/**
 * Zero-dependency RFC 6455 WebSocket + newline-delimited TCP JSON server on 127.0.0.1:25599
 * (or -Dpassthrough.port).
 *
 * <p>Host to Minecraft (JSON, Minecraft coordinates):
 * <ul>
 * <li>{"t":"cam","f":frame,"p":[x,y,z],"r":[yaw,pitch,roll],"fov":vertical degrees,"fp":first person,"pl":[feet x,y,z],"h":body yaw}</li>
 * <li>{"t":"ground","c":[x,z,yBottom,yTop, ...]}: solid columns (barriers)</li>
 * <li>{"t":"clear"}: remove the barriers placed so far</li>
 * <li>{"t":"cmd","c":"time set noon"}: a server command</li>
 * <li>{"t":"key","k":"use|attack|pick|inventory|drop|swap|escape","down":bool}</li>
 * <li>{"t":"slot","n":0-8}, {"t":"scroll","d":+-1}, {"t":"hud","hidden":bool}, {"t":"view","w":px,"h":px}</li>
 * </ul>
 * Minecraft to host: {"t":"hello",...} on connect, {"t":"explosion","pos":[x,y,z],"r":radius}, etc.
 */
public final class HostLink {
	private static final String WS_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
	private static HostLink instance;

	private final int port;
	private final List<ClientConn> clients = new CopyOnWriteArrayList<>();
	private volatile boolean running = true;
	private ServerSocket serverSocket;

	private HostLink(final int port) {
		this.port = port;
	}

	static void launch() {
		int port = Integer.getInteger("passthrough.port", 25599);
		instance = new HostLink(port);
		instance.start();
		Passthrough.events = message -> instance.broadcast(message);
	}

	private void start() {
		Thread acceptThread = new Thread(this::acceptLoop, "Passthrough-HostLink-" + this.port);
		acceptThread.setDaemon(true);
		acceptThread.start();
	}

	private void acceptLoop() {
		try {
			this.serverSocket = new ServerSocket(this.port, 16, InetAddress.getByName("127.0.0.1"));
			this.serverSocket.setReuseAddress(true);
			Passthrough.LOG.info("host link listening on 127.0.0.1:{}", this.port);
			while (this.running) {
				Socket sock = this.serverSocket.accept();
				sock.setTcpNoDelay(true);
				ClientConn conn = new ClientConn(sock);
				this.clients.add(conn);
				Thread t = new Thread(() -> this.handleClient(conn), "Passthrough-Client");
				t.setDaemon(true);
				t.start();
			}
		} catch (IOException e) {
			if (this.running) {
				Passthrough.LOG.warn("host link accept error", e);
			}
		}
	}

	private void handleClient(final ClientConn conn) {
		try {
			InputStream in = conn.socket.getInputStream();
			int first = in.read();
			if (first < 0) {
				return;
			}

			// Detect HTTP WebSocket Upgrade vs raw JSON-line TCP client
			if (first == 'G') {
				ByteArrayOutputStream reqBuf = new ByteArrayOutputStream();
				reqBuf.write(first);
				byte[] window = new byte[4];
				int wLen = 0;
				while (reqBuf.size() < 8192) {
					int b = in.read();
					if (b < 0) {
						return;
					}
					reqBuf.write(b);
					if (wLen < 4) {
						window[wLen++] = (byte) b;
					} else {
						window[0] = window[1];
						window[1] = window[2];
						window[2] = window[3];
						window[3] = (byte) b;
					}
					if (wLen == 4 && window[0] == '\r' && window[1] == '\n' && window[2] == '\r' && window[3] == '\n') {
						break;
					}
				}
				String headers = reqBuf.toString(StandardCharsets.UTF_8);
				String wsKey = null;
				for (String line : headers.split("\r\n")) {
					if (line.toLowerCase(Locale.ROOT).startsWith("sec-websocket-key:")) {
						wsKey = line.substring(line.indexOf(':') + 1).trim();
						break;
					}
				}
				if (wsKey == null) {
					return;
				}
				byte[] sha1 = MessageDigest.getInstance("SHA-1").digest((wsKey + WS_GUID).getBytes(StandardCharsets.UTF_8));
				String accept = Base64.getEncoder().encodeToString(sha1);
				String resp = "HTTP/1.1 101 Switching Protocols\r\n"
					+ "Upgrade: websocket\r\n"
					+ "Connection: Upgrade\r\n"
					+ "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
				conn.socket.getOutputStream().write(resp.getBytes(StandardCharsets.UTF_8));
				conn.socket.getOutputStream().flush();
				conn.webSocket = true;
				this.onOpen(conn);
				this.readWebSocketLoop(conn, in);
			} else {
				conn.webSocket = false;
				this.onOpen(conn);
				ByteArrayOutputStream lineBuf = new ByteArrayOutputStream();
				if (first != '\n' && first != '\r') {
					lineBuf.write(first);
				}
				int b;
				while ((b = in.read()) >= 0) {
					if (b == '\n') {
						String msg = lineBuf.toString(StandardCharsets.UTF_8).trim();
						lineBuf.reset();
						if (!msg.isEmpty()) {
							this.onMessage(conn, msg);
						}
					} else if (b != '\r') {
						lineBuf.write(b);
					}
				}
			}
		} catch (Exception e) {
			// Client disconnected
		} finally {
			this.clients.remove(conn);
			conn.close();
			Passthrough.LOG.info("host disconnected");
		}
	}

	private void readWebSocketLoop(final ClientConn conn, final InputStream in) throws IOException {
		while (this.running && !conn.socket.isClosed()) {
			int b0 = in.read();
			int b1 = in.read();
			if (b0 < 0 || b1 < 0) {
				break;
			}
			int opcode = b0 & 0x0F;
			boolean masked = (b1 & 0x80) != 0;
			long len = b1 & 0x7F;
			if (len == 126) {
				len = ((in.read() & 0xFF) << 8) | (in.read() & 0xFF);
			} else if (len == 127) {
				len = 0;
				for (int i = 0; i < 8; i++) {
					len = (len << 8) | (in.read() & 0xFF);
				}
			}
			byte[] mask = new byte[4];
			if (masked) {
				in.readNBytes(mask, 0, 4);
			}
			if (len > 4 * 1024 * 1024) {
				break;
			}
			byte[] payload = in.readNBytes((int) len);
			if (masked) {
				for (int i = 0; i < payload.length; i++) {
					payload[i] ^= mask[i & 3];
				}
			}
			if (opcode == 0x8) { // Close
				break;
			} else if (opcode == 0x9) { // Ping -> Pong
				conn.sendWsFrame(0xA, payload);
			} else if (opcode == 0x1 || opcode == 0x2) {
				String text = new String(payload, StandardCharsets.UTF_8);
				this.onMessage(conn, text);
			}
		}
	}

	private void onOpen(final ClientConn conn) {
		Passthrough.LOG.info("host connected from {}", conn.socket.getRemoteSocketAddress());
		PassthroughClient.resetOptionsSync();
		String hello = String.format(Locale.ROOT, "{\"t\":\"hello\",\"v\":1,\"shm\":\"%s\",\"pid\":%d}",
			FrameExporter.NAME.replace("\\", "\\\\"), ProcessHandle.current().pid());
		conn.send(hello);
	}

	public void broadcast(final String message) {
		for (ClientConn c : this.clients) {
			c.send(message);
		}
	}

	private void relay(final ClientConn from, final String message) {
		for (ClientConn c : this.clients) {
			if (c != from) {
				c.send(message);
			}
		}
	}

	private void onMessage(final ClientConn conn, final String message) {
		try {
			JsonObject m = JsonParser.parseString(message).getAsJsonObject();
			switch (m.get("t").getAsString()) {
				case "cam" -> HostState.update(m);
				case "idle" -> HostState.clear();
				case "ground" -> WorldBridge.solid(ints(m.getAsJsonArray("c")));
				case "clear" -> WorldBridge.clearSolid();
				case "cmd" -> WorldBridge.command(m.get("c").getAsString());
				case "gta", "gtastate", "gtainfo", "director", "bsl" -> this.relay(conn, message);
				case "blocksync" -> WorldBridge.sync(m.has("r") ? m.get("r").getAsInt() : 48);
				case "projhit" -> {
					JsonArray at = m.getAsJsonArray("pos");
					WorldBridge.projectileHit(m.get("id").getAsInt(), at.get(0).getAsDouble(), at.get(1).getAsDouble(), at.get(2).getAsDouble(),
						m.has("stick") && m.get("stick").getAsBoolean());
				}
				case "peds" -> {
					JsonArray list = m.getAsJsonArray("p");
					double[] flat = new double[list.size() * 4];
					for (int i = 0; i < list.size(); i++) {
						JsonArray e = list.get(i).getAsJsonArray();
						for (int k = 0; k < 4; k++) {
							flat[i * 4 + k] = e.get(k).getAsDouble();
						}
					}
					MobWar.peds(flat);
				}
				case "mobdmg" -> MobWar.damage(m.get("id").getAsInt(), m.get("d").getAsDouble());
				case "spawnmobs" -> MobWar.spawn(m.get("k").getAsString(), m.has("n") ? m.get("n").getAsInt() : 5,
					m.has("rmin") ? m.get("rmin").getAsDouble() : 8.0, m.has("rmax") ? m.get("rmax").getAsDouble() : 16.0,
					m.has("arc") ? m.get("arc").getAsDouble() : 40.0, m.has("yaw") ? m.get("yaw").getAsDouble() : 0.0,
					m.has("at") ? new double[] {m.getAsJsonArray("at").get(0).getAsDouble(), m.getAsJsonArray("at").get(1).getAsDouble(),
						m.getAsJsonArray("at").get(2).getAsDouble()} : null);
				case "mobsclear" -> MobWar.clearMobs();
				case "portal" -> {
					JsonArray at = m.getAsJsonArray("at");
					Nether.buildPortal(at.get(0).getAsDouble(), at.get(1).getAsDouble(), at.get(2).getAsDouble(), m.get("yaw").getAsFloat());
				}
				case "netheroff" -> Nether.stop();
				case "nethersync" -> Nether.resync();
				case "glide" -> WorldBridge.glide(!m.has("on") || m.get("on").getAsBoolean(), m.has("speed") ? m.get("speed").getAsDouble() : 1.2);
				default -> {
					Minecraft minecraft = Minecraft.getInstance();
					minecraft.execute(() -> ClientInput.handle(minecraft, m));
				}
			}
		} catch (RuntimeException e) {
			Passthrough.LOG.warn("bad host message {}: {}", message.length() > 200 ? message.substring(0, 200) : message, e.toString());
		}
	}

	private static int[] ints(final JsonArray a) {
		int[] out = new int[a.size()];
		for (int i = 0; i < out.length; i++) {
			out[i] = a.get(i).getAsInt();
		}
		return out;
	}

	private static final class ClientConn {
		final Socket socket;
		volatile boolean webSocket;

		ClientConn(final Socket socket) {
			this.socket = socket;
		}

		synchronized void send(final String text) {
			try {
				if (this.webSocket) {
					this.sendWsFrame(0x1, text.getBytes(StandardCharsets.UTF_8));
				} else {
					OutputStream out = this.socket.getOutputStream();
					out.write((text + "\n").getBytes(StandardCharsets.UTF_8));
					out.flush();
				}
			} catch (IOException ignored) {
			}
		}

		synchronized void sendWsFrame(final int opcode, final byte[] payload) throws IOException {
			OutputStream out = this.socket.getOutputStream();
			out.write(0x80 | (opcode & 0x0F));
			if (payload.length < 126) {
				out.write(payload.length);
			} else if (payload.length < 65536) {
				out.write(126);
				out.write((payload.length >> 8) & 0xFF);
				out.write(payload.length & 0xFF);
			} else {
				out.write(127);
				long l = payload.length;
				for (int i = 7; i >= 0; i--) {
					out.write((int) ((l >> (8 * i)) & 0xFF));
				}
			}
			out.write(payload);
			out.flush();
		}

		void close() {
			try {
				this.socket.close();
			} catch (IOException ignored) {
			}
		}
	}
}
