package dev.universalmodder.bslbridge;

import com.mojang.brigadier.arguments.StringArgumentType;
import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.command.v2.CommandRegistrationCallback;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.commands.Commands;
import net.minecraft.network.chat.Component;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.phys.Vec3;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Main entrypoint for the BSL v68 <-> Minecraft Java Edition bridge mod.
 * Connects to CustomBrawlServer at ws://localhost:8080 and dispatches live BSL match
 * and character state events onto the Minecraft server thread.
 */
public final class BslBridgeMod implements ModInitializer {
	public static final String MOD_ID = "bslbridge";
	public static final Logger LOG = LoggerFactory.getLogger(MOD_ID);

	private static volatile MinecraftServer activeServer;
	private static BslBridgeClient bridgeClient;

	public static MinecraftServer server() {
		return activeServer;
	}

	public static BslBridgeClient client() {
		return bridgeClient;
	}

	@Override
	public void onInitialize() {
		String url = System.getProperty("bsl.bridge.url", "ws://localhost:8080");

		ServerLifecycleEvents.SERVER_STARTED.register(server -> {
			activeServer = server;
			BslEntityTracker.attach(server);
			bridgeClient = new BslBridgeClient(url);
			bridgeClient.start();
			LOG.info("[BSLBridge] Started WebSocket client targeting {}", url);
		});

		ServerLifecycleEvents.SERVER_STOPPING.register(server -> {
			if (bridgeClient != null) {
				bridgeClient.stop();
				bridgeClient = null;
			}
			BslEntityTracker.detach(server);
			activeServer = null;
			LOG.info("[BSLBridge] Detached bridge and cleaned up custom BSL entities.");
		});

		ServerTickEvents.END_SERVER_TICK.register(server -> {
			BslEntityTracker.tick(server);
		});

		registerCommands();
		LOG.info("[BSLBridge] Mod initialized (default endpoint: {})", url);
	}

	private static void registerCommands() {
		CommandRegistrationCallback.EVENT.register((dispatcher, registryAccess, environment) -> {
			dispatcher.register(Commands.literal("bsl")
				.then(Commands.literal("status")
					.executes(ctx -> {
						boolean connected = bridgeClient != null && bridgeClient.isConnected();
						int count = BslEntityTracker.activeCount();
						Vec3 anchor = BslCoordinateMapper.getAnchor();
						ctx.getSource().sendSuccess(() -> Component.literal(
							String.format("[BSLBridge] Connected: %s | Tracked Brawlers: %d | Arena Anchor: (%.1f, %.1f, %.1f)",
								connected, count, anchor.x, anchor.y, anchor.z)), false);
						return 1;
					}))
				.then(Commands.literal("anchor")
					.executes(ctx -> {
						ServerPlayer player = ctx.getSource().getPlayer();
						if (player != null) {
							BslCoordinateMapper.setAnchor(player.position());
							Vec3 a = BslCoordinateMapper.getAnchor();
							ctx.getSource().sendSuccess(() -> Component.literal(
								String.format("[BSLBridge] Arena center (3150, 4950) anchored at (%.1f, %.1f, %.1f)", a.x, a.y, a.z)), false);
						}
						return 1;
					}))
				.then(Commands.literal("reconnect")
					.executes(ctx -> {
						if (bridgeClient != null) {
							bridgeClient.reconnectNow();
							ctx.getSource().sendSuccess(() -> Component.literal("[BSLBridge] Triggered WebSocket reconnect to ws://localhost:8080"), false);
						}
						return 1;
					}))
				.then(Commands.literal("test")
					.then(Commands.argument("kind", StringArgumentType.word())
						.executes(ctx -> {
							String kind = StringArgumentType.getString(ctx, "kind");
							if (bridgeClient != null && bridgeClient.isConnected()) {
								bridgeClient.sendJson("{\"event\":\"trigger_test\",\"kind\":\"" + kind + "\"}");
								ctx.getSource().sendSuccess(() -> Component.literal("[BSLBridge] Requested test event '" + kind + "' from BSL server"), false);
							} else {
								ctx.getSource().sendFailure(Component.literal("[BSLBridge] Not connected to ws://localhost:8080"));
							}
							return 1;
						}))));
		});
	}
}
