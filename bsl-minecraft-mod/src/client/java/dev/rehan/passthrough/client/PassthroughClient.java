package dev.rehan.passthrough.client;

import dev.rehan.passthrough.Passthrough;
import dev.rehan.passthrough.WorldBridge;
import java.util.List;
import java.util.Optional;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.networking.v1.ServerPlayConnectionEvents;
import net.minecraft.client.CloudStatus;
import net.minecraft.client.InactivityFpsLimit;
import net.minecraft.client.Minecraft;
import net.minecraft.client.Options;
import net.minecraft.client.gui.screens.DeathScreen;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.client.tutorial.TutorialSteps;
import net.minecraft.core.HolderLookup;
import net.minecraft.core.HolderSet;
import net.minecraft.core.registries.Registries;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.LevelSettings;
import net.minecraft.world.level.WorldDataConfiguration;
import net.minecraft.world.level.biome.Biomes;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.levelgen.FlatLevelSource;
import net.minecraft.world.level.levelgen.WorldDimensions;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.levelgen.flat.FlatLayerInfo;
import net.minecraft.world.level.levelgen.flat.FlatLevelGeneratorSettings;
import net.minecraft.world.level.levelgen.presets.WorldPresets;

public class PassthroughClient implements ClientModInitializer {
	/** The world the host plays in: an empty (void) creative world, so only what the player builds is Minecraft. */
	private static final String WORLD = "passthrough";
	/** Run on the server once the player has joined. */
	private static final List<String> SETUP = List.of(
		"gamerule advance_time false",
		"gamerule advance_weather false",
		"gamerule spawn_mobs false",
		"gamerule spawn_monsters false",
		"gamerule spawn_patrols false",
		"gamerule spawn_phantoms false",
		"gamerule spawn_wandering_traders false",
		"gamerule send_command_feedback false",
		"gamerule log_admin_commands false",
		"gamerule keep_inventory true",
		"difficulty normal",
		"gamerule show_advancement_messages false",
		"gamerule player_movement_check false",
		"time set noon",
		"weather clear",
		"clear @a",
		"item replace entity @a hotbar.0 with minecraft:ender_pearl 16",
		"item replace entity @a hotbar.1 with minecraft:diamond_sword[enchantments={sharpness:5}]",
		"item replace entity @a hotbar.2 with minecraft:crossbow[enchantments={multishot:1,quick_charge:3}]",
		"item replace entity @a hotbar.3 with minecraft:bow[enchantments={power:5,infinity:1,flame:1}]",
		"item replace entity @a hotbar.4 with minecraft:tnt 64",
		"item replace entity @a hotbar.5 with minecraft:flint_and_steel",
		"item replace entity @a hotbar.6 with minecraft:fire_charge 64",
		"item replace entity @a hotbar.7 with minecraft:grass_block 64",
		"item replace entity @a hotbar.8 with minecraft:wind_charge 64",
		"item replace entity @a weapon.offhand with minecraft:firework_rocket 64",
		"item replace entity @a inventory.0 with minecraft:netherite_sword[enchantments={sharpness:5,fire_aspect:2}]",
		"item replace entity @a inventory.1 with minecraft:mace[enchantments={density:5,wind_burst:3}]",
		"item replace entity @a inventory.2 with minecraft:trident[enchantments={loyalty:3,impaling:5}]",
		"item replace entity @a inventory.3 with minecraft:diamond_axe[enchantments={sharpness:5}]",
		"item replace entity @a inventory.4 with minecraft:cobblestone 64",
		"item replace entity @a inventory.5 with minecraft:oak_planks 64",
		"item replace entity @a inventory.6 with minecraft:obsidian 64",
		"item replace entity @a inventory.7 with minecraft:diamond_block 64",
		"item replace entity @a inventory.8 with minecraft:snowball 16",
		"item replace entity @a inventory.9 with minecraft:egg 16",
		"item replace entity @a inventory.10 with minecraft:splash_potion[potion_contents={potion:\"minecraft:strong_harming\"}] 16",
		"item replace entity @a inventory.11 with minecraft:golden_apple 64",
		"item replace entity @a inventory.12 with minecraft:lava_bucket",
		"item replace entity @a inventory.13 with minecraft:water_bucket",
		"item replace entity @a inventory.14 with minecraft:firework_rocket 64",
		"item replace entity @a inventory.15 with minecraft:end_crystal 64",
		"item replace entity @a inventory.16 with minecraft:respawn_anchor 64",
		"item replace entity @a inventory.17 with minecraft:glowstone 64",
		"item replace entity @a inventory.18 with minecraft:chorus_fruit 64",
		"item replace entity @a inventory.19 with minecraft:fishing_rod",
		"item replace entity @a inventory.20 with minecraft:anvil 64",
		"item replace entity @a inventory.21 with minecraft:shield",
		"give @a minecraft:arrow 64",
		"attribute @a minecraft:block_interaction_range base set 12.0",
		"attribute @a minecraft:entity_interaction_range base set 8.0"
	);
	private static boolean configured;
	private static boolean worldRequested;
	private static boolean lastScreenOpen;
	private static int lastFov = -1;
	private static double lastSens = -1.0;
	private static String lastKeys = "";
	/** Server ticks until the setup commands run (the player isn't in the player list yet when JOIN fires). */
	private static int setupIn = -1;
	private static int respawnIn;

	public static void resetOptionsSync() {
		lastFov = -1;
		lastSens = -1.0;
		lastKeys = "";
	}

	@Override
	public void onInitializeClient() {
		HostLink.launch();
		ClientTickEvents.END_CLIENT_TICK.register(PassthroughClient::tick);
		ServerPlayConnectionEvents.JOIN.register((handler, sender, server) -> {
			ServerPlayer player = handler.player;
			// never left gliding from a previous session: with no host yet it would glide down into the void
			player.stopFallFlying();
			player.setItemSlot(EquipmentSlot.CHEST, ItemStack.EMPTY);
			player.getAbilities().flying = true;
			player.onUpdateAbilities();
			setupIn = 10;
		});
		ServerTickEvents.END_SERVER_TICK.register(server -> {
			if (setupIn > 0 && --setupIn == 0) {
				SETUP.forEach(WorldBridge::command);
			}
		});
	}

	private static void tick(final Minecraft minecraft) {
		// a death (the void) would leave the death screen over the host's picture: respawn straight away
		if (minecraft.player != null && minecraft.gui.screen() instanceof DeathScreen && --respawnIn <= 0) {
			respawnIn = 40;
			minecraft.player.respawn();
		}

		// Never show the Esc PauseScreen ("Game Menu") in the passthrough world
		if (minecraft.level != null && minecraft.gui.screen() instanceof net.minecraft.client.gui.screens.PauseScreen) {
			minecraft.gui.setScreen(null);
		}

		boolean screenOpen = minecraft.level != null && (
			minecraft.gui.screen() instanceof net.minecraft.client.gui.screens.inventory.InventoryScreen
			|| minecraft.gui.screen() instanceof net.minecraft.client.gui.screens.ChatScreen
			|| minecraft.gui.screen() instanceof net.minecraft.client.gui.screens.options.OptionsScreen
			|| minecraft.gui.screen() instanceof net.minecraft.client.gui.screens.inventory.AbstractContainerScreen
		);
		if (screenOpen != lastScreenOpen) {
			lastScreenOpen = screenOpen;
			Passthrough.events.accept("{\"t\":\"screen\",\"open\":" + screenOpen + "}");
		}

		if (!configured) {
			configured = true;
			configure(minecraft.options);
		}
		if (minecraft.options != null) {
			int curFov = minecraft.options.fov().get();
			double curSens = minecraft.options.sensitivity().get();
			String atk = minecraft.options.keyAttack.saveString();
			String use = minecraft.options.keyUse.saveString();
			String pick = minecraft.options.keyPickItem.saveString();
			String inv = minecraft.options.keyInventory.saveString();
			String swap = minecraft.options.keySwapOffhand.saveString();
			String drop = minecraft.options.keyDrop.saveString();
			String jump = minecraft.options.keyJump.saveString();
			String sneak = minecraft.options.keyShift.saveString();
			String sprint = minecraft.options.keySprint.saveString();
			String chat = minecraft.options.keyChat.saveString();
			String curKeys = atk + "|" + use + "|" + pick + "|" + inv + "|" + swap + "|" + drop + "|" + jump + "|" + sneak + "|" + sprint + "|" + chat;
			if (curFov != lastFov || Math.abs(curSens - lastSens) > 1e-4 || !curKeys.equals(lastKeys)) {
				lastFov = curFov;
				lastSens = curSens;
				lastKeys = curKeys;
				Passthrough.events.accept(String.format(
					java.util.Locale.ROOT,
					"{\"t\":\"options\",\"fov\":%d,\"sens\":%.6f,\"attack\":\"%s\",\"use\":\"%s\",\"pick\":\"%s\",\"inv\":\"%s\",\"swap\":\"%s\",\"drop\":\"%s\",\"jump\":\"%s\",\"sneak\":\"%s\",\"sprint\":\"%s\",\"chat\":\"%s\"}",
					curFov, curSens, atk, use, pick, inv, swap, drop, jump, sneak, sprint, chat
				));
			}
		}
		if (minecraft.getFramerateLimitTracker() != null) {
			minecraft.getFramerateLimitTracker().onInputReceived();
			minecraft.getFramerateLimitTracker().setFramerateLimit(120);
		}

		if (!worldRequested && minecraft.level == null && minecraft.gui.screen() instanceof TitleScreen && !Boolean.getBoolean("passthrough.noAutoWorld")) {
			worldRequested = true;
			openWorld(minecraft);
		}
	}

	/** Settings for sitting behind another game: keep running unfocused without overwriting the user's personal gameplay settings (guiScale, fov, sensitivity, bobView, keybinds). */
	private static void configure(final Options options) {
		options.pauseOnLostFocus = false;
		options.onboardAccessibility = false;
		options.tutorialStep = TutorialSteps.NONE;
		options.cloudStatus().set(CloudStatus.OFF);
		options.vignette().set(false);
		options.improvedTransparency().set(false);
		options.inactivityFpsLimit().set(InactivityFpsLimit.MINIMIZED);
		options.menuBackgroundBlurriness().set(0);
		options.enableVsync().set(false);
		// the host shows ~60-120 fps: rendering faster only competes with it for the GPU
		options.framerateLimit().set(120);
		options.save();
	}

	private static void openWorld(final Minecraft minecraft) {
		if (minecraft.getLevelSource().levelExists(WORLD)) {
			Passthrough.LOG.info("opening world {}", WORLD);
			minecraft.createWorldOpenFlows().openWorld(WORLD, () -> minecraft.gui.setScreen(new TitleScreen()));
		} else {
			Passthrough.LOG.info("creating world {}", WORLD);
			LevelSettings settings = new LevelSettings("Passthrough", GameType.CREATIVE, LevelSettings.DifficultySettings.DEFAULT, true, WorldDataConfiguration.DEFAULT);
			minecraft.createWorldOpenFlows().createFreshLevel(WORLD, settings, new WorldOptions(0L, false, false), PassthroughClient::voidWorld, minecraft.gui.screen());
		}
	}

	/** A flat world with a single layer of air: nothing but what gets built (the host's ground arrives as barriers). */
	private static WorldDimensions voidWorld(final HolderLookup.Provider registries) {
		FlatLevelGeneratorSettings flat = new FlatLevelGeneratorSettings(
			Optional.of(HolderSet.direct()), registries.lookupOrThrow(Registries.BIOME).getOrThrow(Biomes.PLAINS), List.of()
		);
		flat.getLayersInfo().add(new FlatLayerInfo(1, Blocks.AIR));
		flat.updateLayers();
		return WorldPresets.createNormalWorldDimensions(registries).replaceOverworldGenerator(registries, new FlatLevelSource(flat));
	}
}
