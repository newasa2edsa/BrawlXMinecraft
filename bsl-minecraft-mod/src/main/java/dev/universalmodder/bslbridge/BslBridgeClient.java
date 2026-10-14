package dev.universalmodder.bslbridge;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.WebSocket;
import java.time.Duration;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;

/**
 * Asynchronous WebSocket client connecting to CustomBrawlServer at ws://localhost:8080.
 * Never blocks the Minecraft server thread: all parsed JSON payloads are scheduled via server.execute().
 */
public final class BslBridgeClient implements WebSocket.Listener {
	private final URI endpoint;
	private final HttpClient httpClient;
	private final ScheduledExecutorService scheduler;
	private final AtomicBoolean running = new AtomicBoolean(false);
	private final AtomicBoolean connecting = new AtomicBoolean(false);
	private final StringBuilder frameBuffer = new StringBuilder();

	private volatile WebSocket webSocket;

	public BslBridgeClient(final String url) {
		this.endpoint = URI.create(url);
		this.httpClient = HttpClient.newBuilder()
			.connectTimeout(Duration.ofSeconds(3))
			.build();
		this.scheduler = Executors.newSingleThreadScheduledExecutor(r -> {
			Thread t = new Thread(r, "bsl-bridge-ws");
			t.setDaemon(true);
			return t;
		});
	}

	public void start() {
		if (running.compareAndSet(false, true)) {
			connectAsync();
			scheduler.scheduleAtFixedRate(this::ensureConnected, 5, 5, TimeUnit.SECONDS);
		}
	}

	public void stop() {
		running.set(false);
		scheduler.shutdownNow();
		WebSocket ws = this.webSocket;
		this.webSocket = null;
		if (ws != null) {
			try {
				ws.sendClose(WebSocket.NORMAL_CLOSURE, "Mod shutting down");
			} catch (Exception ignored) {
			}
		}
	}

	public boolean isConnected() {
		WebSocket ws = this.webSocket;
		return ws != null && !ws.isInputClosed() && !ws.isOutputClosed();
	}

	public void reconnectNow() {
		WebSocket ws = this.webSocket;
		this.webSocket = null;
		if (ws != null) {
			try {
				ws.abort();
			} catch (Exception ignored) {
			}
		}
		connectAsync();
	}

	public void sendJson(final String json) {
		WebSocket ws = this.webSocket;
		if (ws != null && !ws.isOutputClosed()) {
			ws.sendText(json, true);
		}
	}

	private void ensureConnected() {
		if (running.get() && !isConnected()) {
			connectAsync();
		}
	}

	private void connectAsync() {
		if (!running.get() || !connecting.compareAndSet(false, true)) {
			return;
		}

		httpClient.newWebSocketBuilder()
			.connectTimeout(Duration.ofSeconds(3))
			.buildAsync(endpoint, this)
			.whenComplete((ws, err) -> {
				connecting.set(false);
				if (err != null) {
					BslBridgeMod.LOG.debug("[BSLBridge] Waiting for CustomBrawlServer on {}: {}", endpoint, err.getMessage());
					return;
				}
				this.webSocket = ws;
				BslBridgeMod.LOG.info("[BSLBridge] Connected to CustomBrawlServer at {}", endpoint);
			});
	}

	@Override
	public void onOpen(final WebSocket webSocket) {
		frameBuffer.setLength(0);
		webSocket.request(1);
	}

	@Override
	public CompletionStage<?> onText(final WebSocket webSocket, final CharSequence data, final boolean last) {
		frameBuffer.append(data);
		if (last) {
			String payload = frameBuffer.toString();
			frameBuffer.setLength(0);
			dispatchJsonPayload(payload);
		}
		webSocket.request(1);
		return null;
	}

	@Override
	public CompletionStage<?> onClose(final WebSocket webSocket, final int statusCode, final String reason) {
		this.webSocket = null;
		BslBridgeMod.LOG.info("[BSLBridge] Disconnected from BSL server ({} {})", statusCode, reason);
		return null;
	}

	@Override
	public void onError(final WebSocket webSocket, final Throwable error) {
		this.webSocket = null;
		BslBridgeMod.LOG.debug("[BSLBridge] WebSocket error: {}", error.getMessage());
	}

	private void dispatchJsonPayload(final String rawJson) {
		MinecraftServer server = BslBridgeMod.server();
		if (server == null) {
			return;
		}

		try {
			JsonObject msg = JsonParser.parseString(rawJson).getAsJsonObject();
			String eventType = msg.has("event") ? msg.get("event").getAsString()
				: (msg.has("t") ? msg.get("t").getAsString() : "");

			server.execute(() -> {
				ServerLevel level = server.overworld();
				BslCoordinateMapper.ensureAnchoredToPlayer(server);

				switch (eventType) {
					case "hello" -> BslBridgeMod.LOG.info("[BSLBridge] Handshake complete with {}",
						msg.has("server") ? msg.get("server").getAsString() : "CustomBrawlServer");
					case "character_state" -> BslEntityTracker.onCharacterState(level, msg);
					case "move" -> BslEntityTracker.onMove(level, msg);
					case "match_state" -> BslEntityTracker.onMatchState(level, msg);
					case "attack" -> BslEffectMapper.onAttack(level, msg);
					case "super" -> BslEffectMapper.onSuper(level, msg);
					case "ability" -> BslEffectMapper.onAbility(level, msg);
					default -> BslBridgeMod.LOG.debug("[BSLBridge] Unhandled bridge event: {}", eventType);
				}
			});
		} catch (Exception e) {
			BslBridgeMod.LOG.warn("[BSLBridge] Failed to parse payload {}: {}", rawJson, e.toString());
		}
	}
}
