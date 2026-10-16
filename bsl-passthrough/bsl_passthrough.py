"""All-in-One Setup & First-Person Passthrough Compositor for BSL v68 (MuMu Player) x Minecraft 26.3.

Just run this single script:
    python bsl-passthrough/bsl_passthrough.py

What it does automatically on startup:
1. Copies the compiled `passthrough-0.1.0.jar` into your Modrinth `Fabric 26.3` profile `mods/` folder
   (you still launch Minecraft yourself in Modrinth).
2. Connects to MuMu Player via ADB and applies the First-Person POV & Steve patch (`mumu_steve_patch.py`):
   - Switches Brawl Stars' battle camera to True First-Person POV at eye height `z = 500.0f` (`1.667` blocks)
   - Hides Brawl Stars' 2D battle UI (`CombatGUI`, `CombatHUD`, floating health bars) while keeping touch/WASD controls active
   - Hides your local brawler's 3D mesh & shadow (so you never see Shelly's body) while keeping enemy 3D brawlers visible
   - Disables Shelly's shotgun pellets and muzzle flash in `skills.csv` without crashing
3. Ensures `CustomBrawlServer/config.json` has `SimulateOfflineBattles: false`, restarts `CustomBrawlServer.exe`
   cleanly in the background, and starts the 60 Hz `MumuMemoryBridge` reading `BattleScreen::s_pInstance` (`+0x12f22b8`).
4. Runs the live First-Person passthrough loop:
   - When NO match is happening in Brawl Stars (`BattleScreen::s_pInstance == 0`): sends `{"t":"idle"}` to Minecraft
     so Minecraft shows a BLACK SCREEN, and hides the Win32 desktop overlay completely.
   - When a match IS happening in Brawl Stars (`BattleScreen::s_pInstance != 0`): reads the exact live First-Person
     camera `(eye, target, yaw, pitch, fov)` from `BattleScreen` and sends 60 Hz First-Person camera (`"fp": true`)
     packets to Minecraft so Steve's First-Person hand, held item, crosshair, placed blocks, TNT, and large Hotbar
     are composited 1:1 over the Brawl Stars 3D First-Person arena!
"""
from __future__ import annotations

import atexit
import ctypes
from ctypes import wintypes
import glob
import json
import math
import mmap
import os
import shutil
import signal
import socket
import struct
import subprocess
import sys
import threading
import time
import numpy as np

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.insert(0, os.path.dirname(__file__))
from mumu_steve_patch import apply_mumu_patch, connect_mumu, find_adb, run_adb  # noqa: E402

_brawl_server_proc: subprocess.Popen | None = None
_mc_java_pid: int = 0
_cleanup_done: bool = False


# --------------------------------------------------------------------------- Automatic One-Click Setup
def setup_modrinth_jar():
    """Copy freshly built passthrough-0.1.0.jar into Modrinth 'Fabric 26.3' profile mods/ folder."""
    jar_src = os.path.join(ROOT_DIR, "bsl-minecraft-mod", "build", "libs", "passthrough-0.1.0.jar")
    if not os.path.exists(jar_src):
        print(f"[Setup] Warning: {jar_src} not found.")
        return

    appdata = os.environ.get("APPDATA", "")
    targets = [
        os.path.join(appdata, "ModrinthApp", "profiles", "Fabric 26.3", "mods"),
        os.path.join(appdata, ".minecraft", "mods"),
    ]
    profiles_root = os.path.join(appdata, "ModrinthApp", "profiles")
    if os.path.isdir(profiles_root):
        for p in os.listdir(profiles_root):
            if "26.3" in p:
                p_mods = os.path.join(profiles_root, p, "mods")
                if p_mods not in targets:
                    targets.append(p_mods)
    for mod_dir in targets:
        if os.path.isdir(os.path.dirname(mod_dir)):
            os.makedirs(mod_dir, exist_ok=True)
            old_jar = os.path.join(mod_dir, "bsl-minecraft-bridge-0.1.0.jar")
            if os.path.exists(old_jar):
                try:
                    os.remove(old_jar)
                except OSError:
                    pass
            dst = os.path.join(mod_dir, "passthrough-0.1.0.jar")
            try:
                shutil.copy2(jar_src, dst)
                print(f"[Setup] Installed passthrough-0.1.0.jar -> {dst}")
            except OSError:
                print(f"[Setup] Note: Minecraft or launcher is currently locking {dst}. Restart Minecraft to load the updated mod JAR.")


def ensure_server_config_and_steve_profiles(server_dir: str):
    """Disable SimulateOfflineBattles & UnlockAllCards in config.json and name players 'Steve'."""
    cfg_path = os.path.join(server_dir, "config.json")
    if os.path.exists(cfg_path):
        try:
            with open(cfg_path, "r", encoding="utf-8") as f:
                cfg = json.load(f)
            cfg.setdefault("Server", {})["SimulateOfflineBattles"] = False
            cfg.setdefault("Gameplay", {})["UnlockAllCards"] = False
            with open(cfg_path, "w", encoding="utf-8") as f:
                json.dump(cfg, f, indent=2)
        except Exception:
            pass

    players_dir = os.path.join(server_dir, "data", "players")
    if not os.path.isdir(players_dir):
        return
    for path in glob.glob(os.path.join(players_dir, "*.json")):
        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
            if data.get("Name") in (None, "", "Brawler", "Shelly"):
                data["Name"] = "Steve"
                data["NameSet"] = True
                with open(path, "w", encoding="utf-8") as f:
                    json.dump(data, f, indent=2)
        except Exception:
            pass


def is_port_open(host: str, port: int) -> bool:
    try:
        with socket.create_connection((host, port), timeout=0.3):
            return True
    except OSError:
        return False


def stop_existing_brawl_server():
    """Find any running CustomBrawlServer.exe by exact PID and stop it so the fresh build runs."""
    try:
        res = subprocess.run(
            ["powershell", "-NoProfile", "-Command", "Get-Process CustomBrawlServer -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id"],
            capture_output=True,
            text=True,
            timeout=5,
        )
        for line in res.stdout.splitlines():
            line = line.strip()
            if line.isdigit():
                pid = int(line)
                subprocess.run(["taskkill", "/F", "/PID", str(pid)], capture_output=True, timeout=5)
                print(f"[Setup] Stopped old CustomBrawlServer process (PID {pid}).")
                time.sleep(0.4)
    except Exception:
        pass


def ensure_brawl_server_running() -> subprocess.Popen | None:
    """Start CustomBrawlServer automatically in the background, set up MuMu ADB reverse without restarting adbd,
    and cleanly relaunch com.bsl.v68 so the very first login and first match never hit a stale/severed socket.
    """
    global _brawl_server_proc
    adb = find_adb()
    serial = connect_mumu(adb) if adb else None

    # Stop com.bsl.v68 BEFORE restarting the server so it never sits on a reset socket.
    # Never call `adb root` here because `am force-stop` works as shell (uid 2000) and `adb root` restarts adbd!
    if adb and serial:
        try:
            run_adb(adb, ["shell", "am force-stop com.bsl.v68"], serial, timeout=5)
        except Exception:
            pass

    server_dir = os.path.join(ROOT_DIR, "CustomBrawlServer")
    ensure_server_config_and_steve_profiles(server_dir)
    stop_existing_brawl_server()

    exe_candidates = [
        os.path.join(server_dir, "bin", "Release", "net10.0", "CustomBrawlServer.exe"),
        os.path.join(server_dir, "bin", "Debug", "net10.0", "CustomBrawlServer.exe"),
    ]
    exe_path = next((p for p in exe_candidates if os.path.exists(p)), None)
    cmd = [exe_path] if exe_path else ["dotnet", "run", "--project", server_dir]
    print(f"[Setup] Starting CustomBrawlServer ({' '.join(cmd)})...")
    proc = subprocess.Popen(
        cmd,
        cwd=server_dir,
        stdin=subprocess.DEVNULL,
    )
    _brawl_server_proc = proc
    for _ in range(35):
        if is_port_open("127.0.0.1", 8080) and is_port_open("127.0.0.1", 9339):
            print("[Setup] CustomBrawlServer is online and pre-warmed (TCP/UDP 9339 + Bridge 8080)!")
            break
        time.sleep(0.2)

    # Set up adb reverse, iptables redirect, and launch com.bsl.v68 AFTER CustomBrawlServer is online
    if adb and serial:
        try:
            run_adb(adb, ["reverse", "tcp:9339", "tcp:9339"], serial, timeout=5)
            run_adb(adb, ["reverse", "tcp:8080", "tcp:8080"], serial, timeout=5)
            # Route 192.168.0.2:9339 (and any other IP on port 9339) straight to 127.0.0.1:9339 inside Android!
            from mumu_steve_patch import run_as_root
            run_as_root(
                adb,
                serial,
                "iptables -t nat -D OUTPUT -p tcp --dport 9339 -j DNAT --to-destination 127.0.0.1:9339 2>/dev/null || true; "
                "iptables -t nat -A OUTPUT -p tcp --dport 9339 -j DNAT --to-destination 127.0.0.1:9339 || true; "
                "iptables -t nat -D OUTPUT -p udp --dport 9339 -j DNAT --to-destination 127.0.0.1:9339 2>/dev/null || true; "
                "iptables -t nat -A OUTPUT -p udp --dport 9339 -j DNAT --to-destination 127.0.0.1:9339 || true; "
                "CFG=$(pm path com.bsl.v68 | head -n1 | sed 's/^package://;s|/base\\.apk$|/lib/arm64/libBSL.c.so|'); "
                "if [ -z \"$CFG\" ] || [ ! -f \"$CFG\" ]; then CFG=$(find /data/app -name libBSL.c.so 2>/dev/null | grep com.bsl.v68 | head -n1); fi; "
                "if [ -n \"$CFG\" ] && [ -f \"$CFG\" ]; then echo '{\"interaction\":{\"type\":\"script\",\"path\":\"libBSL.68250_64.so\",\"parameters\":{\"redirectHost\":\"127.0.0.1\",\"redirectPort\":9339,\"offlineBattles\":true}}}' > \"$CFG\"; chmod 755 \"$CFG\"; fi",
            )
            run_adb(
                adb,
                ["shell", "monkey -p com.bsl.v68 -c android.intent.category.LAUNCHER 1"],
                serial,
                timeout=5,
            )
            print("[Setup] Launched BSL v68 in MuMu Player with fresh tcp:9339 tunnel.")
        except Exception:
            pass
    return proc


# --------------------------------------------------------------------------- Live 60Hz MuMu Memory Bridge (/proc/<pid>/mem)
class MumuMemoryBridge:
    """Reads BattleScreen::s_pInstance (libg_base + 0x12f22b8), live First-Person camera floats,
    and enemy LogicCharacterServer positions/HP from com.bsl.v68 memory inside MuMu Player,
    and writes camera look rotation (+0x958), instant 0ms player teleports (own_srv_ptr + own_cli_ptr),
    Brawl Stars TNT wall/bush destruction (+0x1310f88), and brawler damage (+0xf0c).
    Uses two dedicated ADB shell threads (_cam_loop and _brawler_loop) so brawler polling never stalls camera sync.
    """

    S_PINSTANCE_OFF = 0x12F22B8
    EXPORTED_OWN_CLI_OFF = 0x1310778  # Genuine LogicCharacterClientOwn* exported from BattleScreen::updateCamera (0x85a92c)
    EXPORTED_OBJ_MGR_OFF = 0x1310780  # 100% unused .bss slot inside PHDR 3 (< 0x13107e8) written by fp_steve_v14
    PENDING_EXP_OFF = 0x1310788       # (int32 worldX, int32 worldY, int32 radius) consumed by 0x55bf80 hook
    CAM_BLOCK_OFF = 0x8C8
    CHAR_SRV_VTABLES = (0x120C9F0, 0x120C728)

    def __init__(self):
        self.lock = threading.Lock()
        self.connected = False
        self.in_battle = False
        self.eye = (3150.0, -4950.0, 500.0)
        self.target = (3150.0, -2750.0, 500.0)
        self.tp_yaw = 0.0
        self.tp_pitch = 0.0
        self.fov = 60.0
        # List of active living enemy brawlers: (handle, mc_x, mc_z, hp, srv_ptr, bsl_x, bsl_y)
        self.brawlers: list[tuple[int, float, float, int, int, int, int]] = []
        self._pending_look: tuple[float, float] | None = None
        self._pending_teleport: tuple[int, int] | None = None
        self._tp_burst_left: int = 0
        self._teleport_grace_until: float = 0.0
        self._pending_explosion: tuple[int, int, int] | None = None
        self._exp_burst_left: int = 0
        # Pending damage map: handle -> (total_bsl_damage, knock_dx_units, knock_dy_units)
        self._pending_damage: dict[int, tuple[int, int, int]] = {}
        self._pid: int = 0
        self._libg_base: int = 0
        self._s_pinstance: int = 0
        self._own_srv_ptr: int = 0
        self._own_cli_ptr: int = 0
        self._patch_verified = False
        self._stop = False
        self._cam_thread = threading.Thread(target=self._cam_loop, daemon=True)
        self._brawler_thread = threading.Thread(target=self._brawler_loop, daemon=True)
        self._cam_thread.start()
        self._brawler_thread.start()

    def add_look_delta(self, dyaw_rad: float, dpitch_rad: float):
        with self.lock:
            base_yaw, base_pitch = self._pending_look if self._pending_look is not None else (self.tp_yaw, self.tp_pitch)
            new_yaw = base_yaw + dyaw_rad
            while new_yaw > math.pi:
                new_yaw -= 2.0 * math.pi
            while new_yaw < -math.pi:
                new_yaw += 2.0 * math.pi
            new_pitch = max(-1.15, min(1.15, base_pitch + dpitch_rad))
            self._pending_look = (new_yaw, new_pitch)
            self.tp_yaw = new_yaw
            self.tp_pitch = new_pitch

    def queue_player_teleport(self, bx: int, by: int):
        """Queue an instant 0ms Brawl Stars player position teleport (snapping both Server and ClientOwn objects)."""
        with self.lock:
            bx_i = max(300, min(6000, int(bx)))
            by_i = max(300, min(9600, int(by)))
            self._pending_teleport = (bx_i, by_i)
            self._tp_burst_left = 4
            self._teleport_grace_until = time.perf_counter() + 0.25
            dx = float(bx_i) - self.eye[0]
            dy = float(-by_i) - self.eye[1]
            self.eye = (float(bx_i), float(-by_i), self.eye[2])
            self.target = (self.target[0] + dx, self.target[1] + dy, self.target[2])

    def queue_brawl_explosion(self, bx: int, by: int, radius_units: int = 900, radius_tiles: int | None = None):
        """Queue an authoritative Brawl Stars explosion at (bx, by) that destroys walls & bushes and spawns explosion VFX."""
        if radius_tiles is not None:
            radius_units = int(radius_tiles) * 300
        with self.lock:
            bx_i = max(150, min(6150, int(bx)))
            by_i = max(150, min(9750, int(by)))
            rad_i = max(600, min(2400, int(radius_units)))
            self._pending_explosion = (bx_i, by_i, rad_i)
            self._exp_burst_left = 4

    def queue_brawler_damage(self, handle: int, bsl_damage: int, knock_dx: int = 0, knock_dy: int = 0):
        with self.lock:
            prev_dmg, prev_kx, prev_ky = self._pending_damage.get(handle, (0, 0, 0))
            self._pending_damage[handle] = (
                prev_dmg + max(1, int(bsl_damage)),
                prev_kx + int(knock_dx),
                prev_ky + int(knock_dy),
            )

    def snapshot(
        self,
    ) -> tuple[
        bool,
        bool,
        tuple[float, float, float],
        tuple[float, float, float],
        float,
        float,
        float,
        list[tuple[int, float, float, int, int, int, int]],
    ]:
        with self.lock:
            return (
                self.connected,
                self.in_battle,
                self.eye,
                self.target,
                self.tp_yaw,
                self.tp_pitch,
                self.fov,
                list(self.brawlers),
            )

    def _send_cmd(self, proc: subprocess.Popen, cmd: str, marker: str = "__E__") -> list[str]:
        assert proc.stdin is not None and proc.stdout is not None
        full = f"{cmd}; echo {marker}\n"
        proc.stdin.write(full.encode("utf-8"))
        proc.stdin.flush()
        lines: list[str] = []
        while True:
            raw = proc.stdout.readline()
            if not raw:
                raise EOFError("ADB shell closed")
            line = raw.decode("utf-8", errors="replace").strip()
            if line == marker:
                break
            if line:
                lines.append(line)
        return lines

    def _elevate_root(self, proc: subprocess.Popen):
        id_out = "\n".join(self._send_cmd(proc, "id -u"))
        if "0" not in id_out.split():
            for su_bin in ("/system/xbin/su", "/sbin/su", "/system/bin/su", "su"):
                out = "\n".join(self._send_cmd(proc, f"{su_bin} -c 'id -u' 2>/dev/null"))
                if "0" in out.split():
                    assert proc.stdin is not None
                    proc.stdin.write(f"{su_bin}\n".encode("utf-8"))
                    proc.stdin.flush()
                    break

    def _read_bytes(self, proc: subprocess.Popen, pid: int, addr: int, nbytes: int) -> bytes | None:
        if addr < 0x10000:
            return None
        if addr % 8 == 0 and nbytes % 8 == 0:
            bs = 8
        elif addr % 4 == 0 and nbytes % 4 == 0:
            bs = 4
        else:
            bs = 1
        skip = addr // bs
        count = nbytes // bs
        lines = self._send_cmd(
            proc,
            f"dd if=/proc/{pid}/mem bs={bs} skip={skip} count={count} 2>/dev/null | od -An -tx1 -w{nbytes}",
        )
        hex_bytes = "".join(lines).split()
        if len(hex_bytes) < nbytes:
            return None
        return bytes(int(b, 16) for b in hex_bytes[:nbytes])

    def _discover_brawlers(
        self, proc: subprocess.Popen, pid: int, s_pinstance: int, libg_base: int
    ) -> tuple[int, dict[int, int]]:
        """Return (own_srv_ptr, {handle: enemy_LogicCharacterServer*}) for all characters in LogicGameObjectManagerServer."""
        found: dict[int, int] = {}
        own_ptr = 0
        obj_mgr_srv = 0
        if libg_base >= 0x10000:
            exp_buf = self._read_bytes(proc, pid, libg_base + self.EXPORTED_OBJ_MGR_OFF, 8)
            if exp_buf:
                obj_mgr_srv = struct.unpack("<Q", exp_buf)[0]

        if obj_mgr_srv < 0x10000 and s_pinstance >= 0x10000:
            bm_ptr_buf = self._read_bytes(proc, pid, s_pinstance + 0x8F8, 8)
            if bm_ptr_buf:
                battle_mode = struct.unpack("<Q", bm_ptr_buf)[0]
                if battle_mode >= 0x10000:
                    bm_buf = self._read_bytes(proc, pid, battle_mode + 0x28, 16)
                    if bm_buf:
                        _, logic_server = struct.unpack("<2Q", bm_buf)
                        if logic_server >= 0x10000:
                            oms_buf = self._read_bytes(proc, pid, logic_server, 8)
                            if oms_buf:
                                obj_mgr_srv = struct.unpack("<Q", oms_buf)[0]

        if obj_mgr_srv < 0x10000:
            return own_ptr, found

        # [obj_mgr_srv + 0x28] is the dedicated LogicCharacterServer** array; [obj_mgr_srv + 0x34] is its count
        hdr_buf = self._read_bytes(proc, pid, obj_mgr_srv + 0x28, 16)
        if not hdr_buf:
            return own_ptr, found
        chars_ptr = struct.unpack_from("<Q", hdr_buf, 0)[0]
        char_count = struct.unpack_from("<i", hdr_buf, 0x0C)[0]
        if chars_ptr < 0x10000 or char_count <= 0 or char_count > 64:
            return own_ptr, found

        ptrs_buf = self._read_bytes(proc, pid, chars_ptr, char_count * 8)
        if not ptrs_buf:
            return own_ptr, found
        ptrs = [p for p in struct.unpack(f"<{char_count}Q", ptrs_buf) if p >= 0x10000]
        if not ptrs:
            return own_ptr, found

        # [obj_mgr_srv + 0x28] holds ALL LogicGameObjectServer* objects (spawners, crates, projectiles, items, AND characters).
        # Verify each pointer's C++ vtable (+0x0), manager pointer (+0x20 == obj_mgr_srv), and HP (+0x3c4, +0x3c8)
        # so we ONLY include genuine LogicCharacterServer instances (allocated with 0xf10 bytes) and NEVER corrupt heap memory!
        valid_vtable_offs = {0x120C9F0, 0x120C728, 0x120CDB8}
        chk_cmds = [
            f"echo P {idx} $(dd if=/proc/{pid}/mem bs=8 skip={p // 8} count=5 2>/dev/null | od -An -tx1 -w40) $(dd if=/proc/{pid}/mem bs=4 skip={(p + 0x3C4) // 4} count=2 2>/dev/null | od -An -td4 -w8)"
            for idx, p in enumerate(ptrs)
        ]
        chk_lines = self._send_cmd(proc, "; ".join(chk_cmds))
        char_ptrs: list[int] = []
        for ln in chk_lines:
            parts = ln.split()
            if len(parts) < 44 or parts[0] != "P":
                continue
            try:
                idx = int(parts[1])
                hdr_bytes = bytes(int(b, 16) for b in parts[2:42])
                cur_hp = int(parts[42])
                max_hp = int(parts[43])
            except ValueError:
                continue
            if idx < 0 or idx >= len(ptrs):
                continue
            vtbl = struct.unpack_from("<Q", hdr_bytes, 0x00)[0]
            mgr = struct.unpack_from("<Q", hdr_bytes, 0x20)[0]
            vtbl_off = (vtbl - libg_base) & 0xFFFFFFFF
            if mgr == obj_mgr_srv and vtbl_off in valid_vtable_offs and 100 <= max_hp <= 250000 and 0 <= cur_hp <= max_hp:
                char_ptrs.append(ptrs[idx])

        if not char_ptrs:
            return 0, found

        # char_ptrs[0] is ALWAYS the local player's own LogicCharacterServer*!
        # Never put char_ptrs[0] in enemy brawler_map so an invisible proxy Villager never spawns on top of Steve!
        own_ptr = char_ptrs[0]
        for idx, p in enumerate(char_ptrs[1:], start=1):
            h_id = ((p >> 4) & 0x3FFFFFFF) or (idx + 1)
            found[h_id] = p
        return own_ptr, found

    def _cam_loop(self):
        """High-speed 100Hz camera & instant teleport loop."""
        adb = find_adb()
        if not adb:
            return
        while not self._stop:
            serial = connect_mumu(adb)
            if not serial:
                time.sleep(1.5)
                continue
            try:
                # Never call `adb root` here because restarting adbd tears down active adb reverse tunnels!
                run_adb(adb, ["reverse", "tcp:9339", "tcp:9339"], serial, timeout=5)
                run_adb(adb, ["reverse", "tcp:8080", "tcp:8080"], serial, timeout=5)
                proc = subprocess.Popen(
                    [adb, "-s", serial, "shell"],
                    stdin=subprocess.PIPE,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.DEVNULL,
                )
            except Exception:
                time.sleep(1.5)
                continue

            try:
                self._elevate_root(proc)
                pid = 0
                libg_base = 0
                s_pinstance = 0
                own_cli_ptr = 0
                tick_counter = 0

                while not self._stop:
                    if pid == 0 or libg_base == 0:
                        lines = self._send_cmd(
                            proc,
                            "P=$(pidof com.bsl.v68); echo PID:$P; [ -n \"$P\" ] && grep -m1 'libg\\.so' /proc/$P/maps",
                        )
                        pid = 0
                        libg_base = 0
                        for ln in lines:
                            if ln.startswith("PID:"):
                                p_str = ln[4:].strip().split()[0] if ln[4:].strip() else ""
                                if p_str.isdigit():
                                    pid = int(p_str)
                            elif "-" in ln and "libg.so" in ln:
                                addr_hex = ln.split("-", 1)[0].strip()
                                try:
                                    libg_base = int(addr_hex, 16)
                                except ValueError:
                                    libg_base = 0
                        if pid == 0 or libg_base == 0:
                            was_in_battle = self.in_battle
                            with self.lock:
                                self.connected = False
                                self.in_battle = False
                                self.brawlers = []
                                self._pid = 0
                                self._libg_base = 0
                                self._s_pinstance = 0
                                self._own_srv_ptr = 0
                                self._own_cli_ptr = 0
                            if was_in_battle:
                                try:
                                    crash_lines = self._send_cmd(proc, "logcat -d -b crash -t 20 2>/dev/null")
                                    if crash_lines:
                                        print("[Passthrough] Android crash logcat:\n  " + "\n  ".join(crash_lines[-12:]))
                                except Exception:
                                    pass
                            time.sleep(0.5)
                            continue
                        with self.lock:
                            self.connected = True
                            self._pid = pid
                            self._libg_base = libg_base

                    # Check s_pinstance when not in battle or every 30th tick during battle
                    if s_pinstance < 0x10000 or (tick_counter % 30 == 0):
                        inst_skip8 = (libg_base + self.S_PINSTANCE_OFF) // 8
                        lines = self._send_cmd(
                            proc,
                            f"dd if=/proc/{pid}/mem bs=8 skip={inst_skip8} count=1 2>/dev/null | od -An -tx1 -w8",
                        )
                        if not lines:
                            pid = 0
                            s_pinstance = 0
                            continue
                        hex_bytes = "".join(lines).split()
                        if len(hex_bytes) < 8:
                            pid = 0
                            s_pinstance = 0
                            continue
                        new_inst = struct.unpack("<Q", bytes(int(b, 16) for b in hex_bytes[:8]))[0]
                        if new_inst == 0 or new_inst < 0x10000:
                            s_pinstance = 0
                            own_cli_ptr = 0
                            with self.lock:
                                self.in_battle = False
                                self._s_pinstance = 0
                                self._own_srv_ptr = 0
                                self._own_cli_ptr = 0
                                self._pending_look = None
                                self._pending_teleport = None
                                self._tp_burst_left = 0
                                self._pending_explosion = None
                                self._exp_burst_left = 0
                                self._pending_damage.clear()
                                self.brawlers = []
                            time.sleep(0.08)
                            continue
                        if s_pinstance < 0x10000 or s_pinstance != new_inst:
                            s_pinstance = new_inst
                            own_cli_ptr = 0
                            exp_seek4 = (libg_base + self.PENDING_EXP_OFF) // 4
                            self._send_cmd(
                                proc,
                                f"printf '\\x00\\x00\\x00\\x00' | dd of=/proc/{pid}/mem bs=4 seek={exp_seek4} count=1 conv=notrunc 2>/dev/null",
                            )
                            yp_buf = self._read_bytes(proc, pid, s_pinstance + 0x958, 8)
                            if yp_buf:
                                init_yaw, init_pitch = struct.unpack("<2f", yp_buf)
                                if math.isfinite(init_yaw) and math.isfinite(init_pitch):
                                    with self.lock:
                                        self.tp_yaw = init_yaw
                                        self.tp_pitch = init_pitch
                        with self.lock:
                            self._s_pinstance = s_pinstance

                    # Cache own_cli_ptr (LogicCharacterClientOwn* exported to libg_base + 0x1310778) for 0ms instant Ender Pearl teleports
                    if self.in_battle and (own_cli_ptr < 0x10000 or tick_counter % 60 == 0):
                        cli_buf = self._read_bytes(proc, pid, libg_base + self.EXPORTED_OWN_CLI_OFF, 8)
                        if cli_buf:
                            c_ptr = struct.unpack("<Q", cli_buf)[0] & 0x00FFFFFFFFFFFFFF
                            if c_ptr >= 0x10000:
                                own_cli_ptr = c_ptr
                                with self.lock:
                                    self._own_cli_ptr = c_ptr

                    tick_counter += 1
                    cam_skip8 = (s_pinstance + self.CAM_BLOCK_OFF) // 8
                    look_seek4 = (s_pinstance + 0x958) // 4
                    with self.lock:
                        battle_ready = self.in_battle
                        pending_look = self._pending_look if battle_ready else None
                        if battle_ready:
                            self._pending_look = None
                        if battle_ready and self._tp_burst_left > 0 and self._pending_teleport is not None:
                            pending_tp = self._pending_teleport
                            self._tp_burst_left -= 1
                            if self._tp_burst_left <= 0:
                                self._pending_teleport = None
                        else:
                            pending_tp = None
                        if battle_ready and self._exp_burst_left > 0 and self._pending_explosion is not None:
                            pending_exp = self._pending_explosion
                            self._exp_burst_left -= 1
                            if self._exp_burst_left <= 0:
                                self._pending_explosion = None
                        else:
                            pending_exp = None
                        target_own_ptr = self._own_srv_ptr

                    cmd_parts = []
                    if pending_look is not None:
                        nyaw, npitch = pending_look
                        raw16 = struct.pack("<4f", nyaw, npitch, nyaw, npitch)
                        printf_str = "".join(f"\\x{b:02x}" for b in raw16)
                        cmd_parts.append(
                            f"printf '{printf_str}' | dd of=/proc/{pid}/mem bs=4 seek={look_seek4} count=4 conv=notrunc 2>/dev/null"
                        )

                    if pending_exp is not None:
                        exp_bx, exp_by, exp_rad = pending_exp
                        exp_seek4 = (libg_base + self.PENDING_EXP_OFF) // 4
                        raw_exp = struct.pack("<3i", exp_bx, exp_by, exp_rad)
                        p_exp = "".join(f"\\x{b:02x}" for b in raw_exp)
                        cmd_parts.append(
                            f"printf '{p_exp}' | dd of=/proc/{pid}/mem bs=4 seek={exp_seek4} count=3 conv=notrunc 2>/dev/null"
                        )

                    if pending_tp is not None:
                        tp_bx, tp_by = pending_tp
                        if target_own_ptr < 0x10000:
                            own_p, _ = self._discover_brawlers(proc, pid, s_pinstance, libg_base)
                            if own_p >= 0x10000:
                                target_own_ptr = own_p
                                with self.lock:
                                    self._own_srv_ptr = own_p
                        raw_tp = struct.pack("<2i", tp_bx, tp_by)
                        p_tp = "".join(f"\\x{b:02x}" for b in raw_tp)
                        if target_own_ptr >= 0x10000:
                            # 1) Snap LogicCharacterServer m_x/m_y (+0x38)
                            own_pos_seek4 = (target_own_ptr + 0x38) // 4
                            cmd_parts.append(
                                f"printf '{p_tp}' | dd of=/proc/{pid}/mem bs=4 seek={own_pos_seek4} count=2 conv=notrunc 2>/dev/null"
                            )
                        if own_cli_ptr >= 0x10000:
                            # 2) Snap LogicCharacterClient m_x/m_y (+0x30, +0x34)
                            cli_pos_seek4 = (own_cli_ptr + 0x30) // 4
                            cmd_parts.append(
                                f"printf '{p_tp}' | dd of=/proc/{pid}/mem bs=4 seek={cli_pos_seek4} count=2 conv=notrunc 2>/dev/null"
                            )
                        # 3) Snap BattleScreen camera (+0x980, +0x8c8, and target +0x8d4)
                        cam_pos_seek4 = (s_pinstance + 0x980) // 4
                        eye_pos_seek4 = (s_pinstance + 0x8C8) // 4
                        raw_cam_tp = struct.pack("<2f", float(tp_bx), float(-tp_by))
                        p_cam_tp = "".join(f"\\x{b:02x}" for b in raw_cam_tp)
                        cmd_parts.append(
                            f"printf '{p_cam_tp}' | dd of=/proc/{pid}/mem bs=4 seek={cam_pos_seek4} count=2 conv=notrunc 2>/dev/null"
                        )
                        cmd_parts.append(
                            f"printf '{p_cam_tp}' | dd of=/proc/{pid}/mem bs=4 seek={eye_pos_seek4} count=2 conv=notrunc 2>/dev/null"
                        )
                        dx = float(tp_bx) - self.eye[0]
                        dy = float(-tp_by) - self.eye[1]
                        new_target_x = self.target[0] + dx
                        new_target_y = self.target[1] + dy
                        raw_tgt_tp = struct.pack("<2f", new_target_x, new_target_y)
                        p_tgt_tp = "".join(f"\\x{b:02x}" for b in raw_tgt_tp)
                        tgt_pos_seek4 = (s_pinstance + 0x8D4) // 4
                        cmd_parts.append(
                            f"printf '{p_tgt_tp}' | dd of=/proc/{pid}/mem bs=4 seek={tgt_pos_seek4} count=2 conv=notrunc 2>/dev/null"
                        )

                    # Read only the 24-byte eye+target block (3x 8-byte words) for ultra-fast, zero-jitter camera sync!
                    cmd_parts.append(
                        f"dd if=/proc/{pid}/mem bs=8 skip={cam_skip8} count=3 2>/dev/null | od -An -tx1 -w24"
                    )
                    cam_lines = self._send_cmd(proc, "; ".join(cmd_parts))
                    cam_hex = "".join(cam_lines).split()
                    if len(cam_hex) < 24:
                        s_pinstance = 0
                        with self.lock:
                            self.in_battle = False
                            self._s_pinstance = 0
                        continue

                    buf = bytes(int(b, 16) for b in cam_hex[:24])
                    eye_x, eye_y, eye_z, target_x, target_y, target_z = struct.unpack_from("<6f", buf, 0)
                    # Wait until BattleScreen::updateCamera has set eye_z (~500.0) so we never race during match loading!
                    if all(math.isfinite(v) for v in (eye_x, eye_y, eye_z, target_x, target_y, target_z)) and eye_z > 100.0:
                        with self.lock:
                            self.in_battle = True
                            if time.perf_counter() >= self._teleport_grace_until and self._pending_teleport is None:
                                self.eye = (eye_x, eye_y, eye_z)
                                self.target = (target_x, target_y, target_z)
                            self.fov = 60.0
            except Exception:
                with self.lock:
                    self.connected = False
                    self.in_battle = False
                    self.brawlers = []
                    self._pid = 0
                    self._s_pinstance = 0
                try:
                    proc.kill()
                except Exception:
                    pass
                time.sleep(1.0)

    def _brawler_loop(self):
        """Background ADB shell thread that polls enemy brawler positions/HP and writes damage without blocking _cam_loop."""
        adb = find_adb()
        if not adb:
            return
        while not self._stop:
            with self.lock:
                pid = self._pid
                libg_base = self._libg_base
            if pid == 0 or libg_base == 0:
                time.sleep(0.3)
                continue

            serial = connect_mumu(adb)
            if not serial:
                time.sleep(1.0)
                continue
            try:
                proc = subprocess.Popen(
                    [adb, "-s", serial, "shell"],
                    stdin=subprocess.PIPE,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.DEVNULL,
                )
            except Exception:
                time.sleep(1.0)
                continue

            try:
                self._elevate_root(proc)
                bound_pid = 0
                brawler_map: dict[int, int] = {}
                brawler_pos_cache: dict[int, tuple[int, int, int]] = {}
                last_discover = 0.0

                while not self._stop:
                    with self.lock:
                        pid = self._pid
                        libg_base = self._libg_base
                        s_pinstance = self._s_pinstance
                        in_battle = self.in_battle
                        eye_x, eye_y, _ = self.eye

                    if pid == 0 or libg_base == 0 or s_pinstance < 0x10000 or not in_battle:
                        brawler_map.clear()
                        brawler_pos_cache.clear()
                        with self.lock:
                            self.brawlers = []
                            self._pending_damage.clear()
                            self._own_srv_ptr = 0
                        time.sleep(0.1)
                        continue

                    if bound_pid != pid:
                        bound_pid = pid
                        self._send_cmd(
                            proc,
                            f"M=/proc/{pid}/mem; bp() {{ echo B $1 $(dd if=$M bs=4 skip=$2 count=2 2>/dev/null | od -An -td4 -w8) $(dd if=$M bs=4 skip=$3 count=1 2>/dev/null | od -An -td4 -w4); }}",
                        )

                    now_t = time.perf_counter()
                    if not brawler_map or (now_t - last_discover) > 2.5:
                        own_p, new_map = self._discover_brawlers(proc, pid, s_pinstance, libg_base)
                        if own_p >= 0x10000:
                            with self.lock:
                                self._own_srv_ptr = own_p
                        if new_map:
                            if not brawler_map:
                                print(
                                    f"[Passthrough] Locked onto player character + {len(new_map)} enemy brawlers in LogicGameObjectManagerServer!"
                                )
                            brawler_map = new_map
                        last_discover = now_t

                    with self.lock:
                        pending_dmg = dict(self._pending_damage)
                        self._pending_damage.clear()

                    cmd_parts = []
                    for h_id, (dmg_val, kx, ky) in pending_dmg.items():
                        srv_ptr = brawler_map.get(h_id)
                        if srv_ptr:
                            dmg_seek4 = (srv_ptr + 0xF0C) // 4
                            raw_dmg = struct.pack("<i", dmg_val)
                            p_dmg = "".join(f"\\x{b:02x}" for b in raw_dmg)
                            cmd_parts.append(
                                f"printf '{p_dmg}' | dd of=/proc/{pid}/mem bs=4 seek={dmg_seek4} count=1 conv=notrunc 2>/dev/null"
                            )
                            if (kx != 0 or ky != 0) and h_id in brawler_pos_cache:
                                cur_x, cur_y, _ = brawler_pos_cache[h_id]
                                new_x = max(150, cur_x + kx)
                                new_y = max(150, cur_y + ky)
                                pos_seek4 = (srv_ptr + 0x38) // 4
                                raw_pos = struct.pack("<2i", new_x, new_y)
                                p_pos = "".join(f"\\x{b:02x}" for b in raw_pos)
                                cmd_parts.append(
                                    f"printf '{p_pos}' | dd of=/proc/{pid}/mem bs=4 seek={pos_seek4} count=2 conv=notrunc 2>/dev/null"
                                )

                    for h_id, srv_p in brawler_map.items():
                        cmd_parts.append(f"bp {h_id} {(srv_p + 0x38) // 4} {(srv_p + 0x3C4) // 4}")

                    if cmd_parts:
                        b_lines = self._send_cmd(proc, "; ".join(cmd_parts))
                        for bln in b_lines:
                            parts = bln.split()
                            if len(parts) >= 5 and parts[0] == "B":
                                try:
                                    h_id = int(parts[1])
                                    bx = int(parts[2])
                                    by = int(parts[3])
                                    bhp = int(parts[4])
                                except ValueError:
                                    continue
                                brawler_pos_cache[h_id] = (bx, by, bhp)

                        own_bx, own_by = eye_x, -eye_y
                        active_list: list[tuple[int, float, float, int, int, int, int]] = []
                        for h_id, srv_p in brawler_map.items():
                            cached = brawler_pos_cache.get(h_id)
                            if not cached:
                                continue
                            bx, by, bhp = cached
                            if math.hypot(bx - own_bx, by - own_by) < 135.0:
                                continue
                            if bhp > 0 and 0 <= bx <= 25000 and 0 <= by <= 25000:
                                mc_x = (bx - 3150.0) / 300.0
                                mc_z = (by - 4950.0) / 300.0
                                active_list.append((h_id, mc_x, mc_z, bhp, srv_p, bx, by))
                        with self.lock:
                            self.brawlers = active_list
                    time.sleep(0.03)
            except Exception:
                try:
                    proc.kill()
                except Exception:
                    pass
                time.sleep(0.5)


# --------------------------------------------------------------------------- Shared Memory Reader (Local\MCPassthroughFrame)
SHM_NAME = "Local\\MCPassthroughFrame"
SHM_MAGIC = 0x5450434D
SHM_HEADER = 4096
SLOT_DESC = 256
SLOT_DESC_BYTES = 128


class MCFrameReader:
    def __init__(self):
        self.mm = None
        self.header = SHM_HEADER
        self.slots = 3
        self.stride = 0
        self._logged_frame_size: tuple[int, int] | None = None
        self._last_seq: int = -1

    def ensure_open(self) -> bool:
        global _mc_java_pid
        if self.mm is not None:
            return True
        try:
            probe = mmap.mmap(-1, SHM_HEADER, tagname=SHM_NAME, access=mmap.ACCESS_READ)
            magic, version, header, slots, stride, maxw, maxh = struct.unpack_from("<iiiiqii", probe, 0)
            shm_pid = struct.unpack_from("<i", probe, 44)[0]
            probe.close()
            if magic != SHM_MAGIC:
                return False
            if shm_pid > 0:
                _mc_java_pid = shm_pid
            self.header = header
            self.slots = slots
            self.stride = stride
            self.mm = mmap.mmap(-1, header + stride * slots, tagname=SHM_NAME, access=mmap.ACCESS_READ)
            print(f"[Passthrough] Attached to shared memory {SHM_NAME} (Minecraft PID={_mc_java_pid})")
            return True
        except OSError:
            return False

    def latest_bgra(self) -> tuple[int, int, np.ndarray] | None:
        if not self.ensure_open():
            return None
        for _ in range(3):
            slot = struct.unpack_from("<i", self.mm, 40)[0]
            if slot < 0:
                return None
            d = SLOT_DESC + SLOT_DESC_BYTES * slot
            seq = struct.unpack_from("<q", self.mm, d)[0]
            if seq & 1:
                continue
            if seq == self._last_seq:
                return None
            mc_frame, host_frame, w, h, near, far, fov, flags = struct.unpack_from("<qqiifffi", self.mm, d + 8)
            if w <= 0 or h <= 0 or w > 3840 or h > 2160:
                return None
            if self._logged_frame_size != (w, h):
                self._logged_frame_size = (w, h)
                print(f"[Passthrough] Active Minecraft frame resolution: {w}x{h} (1080p display mode)")
            n = w * h * 4
            base = self.header + self.stride * slot
            buf = memoryview(self.mm)[base : base + 3 * n]
            world = np.frombuffer(buf, np.uint8, n, 0).reshape(h, w, 4)
            over = np.frombuffer(buf, np.uint8, n, 2 * n).reshape(h, w, 4)
            if flags & 2:
                # Rows are bottom-up in OpenGL readback; flip to top-down for Win32 top-down DIB
                world = world[::-1]
                over = over[::-1]

            # In First-Person mode (`fp=true`), `over` contains the 3D Minecraft world + Steve's
            # First-Person right hand / held item (`render3dHud`) + center crosshair + bottom hotbar!
            rgba = np.array(over, copy=True)

            # Check ONLY the bottom-center hotbar region (and left-of-hotbar margin, away from Steve's
            # right-hand weapon at the bottom-right) so the hotbar stays a constant 2x normal size
            # whether attacking or standing still!
            if w >= 740 and h >= 200:
                src_w = min(368, w // 2)
                src_h = min(48, h // 2)
                x0 = (w - src_w) // 2
                hb_src = over[-src_h:, x0 : x0 + src_w, :]
                wd_src = world[-src_h:, x0 : x0 + src_w, :]
                hb_diff = np.any(hb_src[:, :, :3] != wd_src[:, :, :3], axis=2)

                # Check left margin [w//2 - 340 : x0 - 12] (left side of screen, untouched by right-hand weapon)
                # to see if Minecraft is already rendering a large guiScale>=3 hotbar
                lm_x0 = max(0, w // 2 - 340)
                lm_x1 = max(lm_x0, x0 - 12)
                already_large = False
                if lm_x1 > lm_x0:
                    already_large = bool(
                        np.any(over[-src_h:, lm_x0:lm_x1, :3] != world[-src_h:, lm_x0:lm_x1, :3])
                    )

                if not already_large and np.any(hb_diff):
                    hb_2x = np.repeat(np.repeat(hb_src, 2, axis=0), 2, axis=1)
                    mask_2x = np.repeat(np.repeat(hb_diff, 2, axis=0), 2, axis=1)
                    dst_h, dst_w, _ = hb_2x.shape
                    dx0 = (w - dst_w) // 2
                    dst_slice = rgba[-dst_h:, dx0 : dx0 + dst_w, :]
                    dst_slice[mask_2x] = hb_2x[mask_2x]

            comp = np.ascontiguousarray(rgba[:, :, [2, 1, 0, 3]])  # RGBA -> BGRA
            if struct.unpack_from("<q", self.mm, d)[0] != seq:
                continue
            self._last_seq = seq
            return w, h, comp
        return None


# --------------------------------------------------------------------------- Win32 Layered Overlay Window
user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32
kernel32 = ctypes.windll.kernel32

WS_POPUP = 0x80000000
WS_VISIBLE = 0x10000000
WS_EX_LAYERED = 0x00080000
WS_EX_TRANSPARENT = 0x00000020
WS_EX_TOPMOST = 0x00000008
WS_EX_TOOLWINDOW = 0x00000080
WS_EX_APPWINDOW = 0x00040000
WS_EX_NOACTIVATE = 0x08000000
GWL_EXSTYLE = -20
LWA_ALPHA = 0x00000002
ULW_ALPHA = 0x00000002
AC_SRC_OVER = 0x00
AC_SRC_ALPHA = 0x01
DIB_RGB_COLORS = 0
SW_HIDE = 0
SW_SHOWNOACTIVATE = 4
SW_RESTORE = 9
WM_MOUSEMOVE = 0x0200


class POINT(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]


class SIZE(ctypes.Structure):
    _fields_ = [("cx", ctypes.c_long), ("cy", ctypes.c_long)]


class BLENDFUNCTION(ctypes.Structure):
    _fields_ = [
        ("BlendOp", ctypes.c_ubyte),
        ("BlendFlags", ctypes.c_ubyte),
        ("SourceConstantAlpha", ctypes.c_ubyte),
        ("AlphaFormat", ctypes.c_ubyte),
    ]


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wintypes.DWORD),
        ("biWidth", ctypes.c_long),
        ("biHeight", ctypes.c_long),
        ("biPlanes", wintypes.WORD),
        ("biBitCount", wintypes.WORD),
        ("biCompression", wintypes.DWORD),
        ("biSizeImage", wintypes.DWORD),
        ("biXPelsPerMeter", ctypes.c_long),
        ("biYPelsPerMeter", ctypes.c_long),
        ("biClrUsed", wintypes.DWORD),
        ("biClrImportant", wintypes.DWORD),
    ]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", wintypes.DWORD * 3)]


WNDPROCTYPE = ctypes.WINFUNCTYPE(ctypes.c_longlong, wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM)

user32.DefWindowProcW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.DefWindowProcW.restype = ctypes.c_longlong


class WNDCLASSEXW(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.UINT),
        ("style", wintypes.UINT),
        ("lpfnWndProc", WNDPROCTYPE),
        ("cbClsExtra", ctypes.c_int),
        ("cbWndExtra", ctypes.c_int),
        ("hInstance", wintypes.HINSTANCE),
        ("hIcon", wintypes.HICON),
        ("hCursor", wintypes.HANDLE),
        ("hbrBackground", wintypes.HBRUSH),
        ("lpszMenuName", wintypes.LPCWSTR),
        ("lpszClassName", wintypes.LPCWSTR),
        ("hIconSm", wintypes.HICON),
    ]


_cached_mumu_hwnd: int | None = None
_last_mumu_enum_time: float = 0.0
_cached_mc_hwnd: int | None = None
_mc_hidden_from_taskbar: bool = False
_last_mc_window_check: float = 0.0
_last_mc_keepalive: float = 0.0


def hide_and_unthrottle_minecraft_window():
    """Hide the Minecraft window & taskbar icon while keeping it un-minimized so OpenGL renders at 60-120 FPS."""
    global _cached_mc_hwnd, _mc_hidden_from_taskbar, _last_mc_window_check, _last_mc_keepalive, _mc_java_pid
    now = time.perf_counter()

    # Periodically post a synthetic mouse-move to Minecraft's HWND so FramerateLimitTracker never enters AFK throttle
    if _cached_mc_hwnd and user32.IsWindow(_cached_mc_hwnd):
        if (now - _last_mc_keepalive) >= 2.0:
            _last_mc_keepalive = now
            if user32.IsIconic(_cached_mc_hwnd):
                user32.ShowWindow(_cached_mc_hwnd, SW_RESTORE)
            user32.PostMessageW(_cached_mc_hwnd, WM_MOUSEMOVE, 0, 0x00640064)
        if _mc_hidden_from_taskbar and (now - _last_mc_window_check) < 3.0:
            return
    else:
        _cached_mc_hwnd = None
        _mc_hidden_from_taskbar = False

    if (now - _last_mc_window_check) < 1.0:
        return
    _last_mc_window_check = now

    found_mc: list[tuple[int, int, str]] = []

    @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def enum_mc_cb(hwnd, lparam):
        length = user32.GetWindowTextLengthW(hwnd)
        if length <= 0:
            return True
        buf = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(hwnd, buf, length + 1)
        title = buf.value
        t_low = title.lower()
        if any(bad in t_low for bad in ("visual studio", "code", "powershell", "cmd.exe", "passthrough", "modrinth")):
            return True
        proc_id = wintypes.DWORD(0)
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(proc_id))
        w_pid = int(proc_id.value)
        if (_mc_java_pid > 0 and w_pid == _mc_java_pid) or ("minecraft" in t_low and ("26.3" in t_low or "fabric" in t_low or "singleplayer" in t_low)):
            found_mc.append((hwnd, w_pid, title))
        return True

    user32.EnumWindows(enum_mc_cb, 0)
    if not found_mc:
        return

    hwnd, w_pid, title = found_mc[0]
    if _mc_java_pid <= 0 and w_pid > 0:
        _mc_java_pid = w_pid
    _cached_mc_hwnd = hwnd

    # 1. If the user minimized Minecraft, restore it first! Minimized windows cause SDL/Minecraft to throttle to 10 FPS.
    if user32.IsIconic(hwnd):
        user32.ShowWindow(hwnd, SW_RESTORE)

    # 2. Strip WS_EX_APPWINDOW and apply WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE
    #    with alpha=1/255 so the Minecraft window has NO taskbar icon and is 100% invisible & click-through on screen!
    ex_style = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
    target_bits = WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE
    if not _mc_hidden_from_taskbar or (ex_style & WS_EX_APPWINDOW) or (ex_style & target_bits) != target_bits:
        new_ex = (ex_style & ~WS_EX_APPWINDOW) | target_bits
        user32.ShowWindow(hwnd, SW_HIDE)
        user32.SetWindowLongW(hwnd, GWL_EXSTYLE, new_ex)
        user32.SetLayeredWindowAttributes(hwnd, 0, 1, LWA_ALPHA)
        user32.ShowWindow(hwnd, SW_SHOWNOACTIVATE)
        user32.PostMessageW(hwnd, WM_MOUSEMOVE, 0, 0x00640064)
        _mc_hidden_from_taskbar = True
        print(f"[Passthrough] Hid Minecraft window & taskbar icon ('{title}', PID={_mc_java_pid}) at full 60+ FPS.")


def cleanup_all_processes():
    """Stop the Minecraft Java process and CustomBrawlServer when bsl_passthrough.py exits."""
    global _cleanup_done, _brawl_server_proc, _mc_java_pid
    if _cleanup_done:
        return
    _cleanup_done = True

    if _brawl_server_proc is not None:
        try:
            if _brawl_server_proc.poll() is None:
                _brawl_server_proc.terminate()
                print("[Cleanup] Stopped CustomBrawlServer.")
        except Exception:
            pass

    # Discover Minecraft PID from shared memory if not yet recorded
    if _mc_java_pid <= 0:
        try:
            probe = mmap.mmap(-1, SHM_HEADER, tagname=SHM_NAME, access=mmap.ACCESS_READ)
            magic = struct.unpack_from("<i", probe, 0)[0]
            shm_pid = struct.unpack_from("<i", probe, 44)[0]
            probe.close()
            if magic == SHM_MAGIC and shm_pid > 0:
                _mc_java_pid = shm_pid
        except Exception:
            pass

    if _mc_java_pid > 0:
        try:
            subprocess.run(["taskkill", "/F", "/PID", str(_mc_java_pid)], capture_output=True, timeout=4)
            print(f"[Cleanup] Stopped Minecraft Java process (PID {_mc_java_pid}).")
        except Exception:
            pass


CTRL_HANDLER_TYPE = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.DWORD)


def _win_console_ctrl_handler(ctrl_type: int) -> bool:
    cleanup_all_processes()
    return False


_console_ctrl_ref = CTRL_HANDLER_TYPE(_win_console_ctrl_handler)
try:
    kernel32.SetConsoleCtrlHandler(_console_ctrl_ref, True)
except Exception:
    pass
atexit.register(cleanup_all_processes)
for _sig in (signal.SIGINT, signal.SIGTERM, getattr(signal, "SIGBREAK", signal.SIGINT)):
    try:
        signal.signal(_sig, lambda s, f: (cleanup_all_processes(), sys.exit(0)))
    except Exception:
        pass


def find_mumu_window() -> tuple[int, tuple[int, int, int, int]] | None:
    """Find the running MuMu Player window with fast HWND caching so EnumWindows isn't called every frame."""
    global _cached_mumu_hwnd, _last_mumu_enum_time
    now = time.perf_counter()
    if _cached_mumu_hwnd and (now - _last_mumu_enum_time) < 0.5:
        if user32.IsWindowVisible(_cached_mumu_hwnd) and not user32.IsIconic(_cached_mumu_hwnd):
            rect = wintypes.RECT()
            if user32.GetWindowRect(_cached_mumu_hwnd, ctypes.byref(rect)):
                w = rect.right - rect.left
                h = rect.bottom - rect.top
                if w >= 400 and h >= 300:
                    return _cached_mumu_hwnd, (rect.left, rect.top, w, h)

    _last_mumu_enum_time = now
    found = []

    @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def enum_cb(hwnd, lparam):
        if not user32.IsWindowVisible(hwnd) or user32.IsIconic(hwnd):
            return True
        length = user32.GetWindowTextLengthW(hwnd)
        if length <= 0:
            return True
        buf = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(hwnd, buf, length + 1)
        title = buf.value.lower()
        if any(bad in title for bad in ("visual studio", "code", "powershell", "cmd.exe", "minecraft", "passthrough", "modrinth")):
            return True
        if any(k in title for k in ("mumu", "nemu", "brawl stars")):
            rect = wintypes.RECT()
            user32.GetWindowRect(hwnd, ctypes.byref(rect))
            w = rect.right - rect.left
            h = rect.bottom - rect.top
            if w >= 400 and h >= 300:
                found.append((w * h, hwnd, (rect.left, rect.top, w, h), buf.value))
        return True

    user32.EnumWindows(enum_cb, 0)
    if not found:
        _cached_mumu_hwnd = None
        return None
    found.sort(reverse=True)
    _, hwnd, rect, _ = found[0]
    _cached_mumu_hwnd = hwnd
    return hwnd, rect


WM_INPUT = 0x00FF
WM_MOUSEACTIVATE = 0x0021
WM_MOUSEWHEEL = 0x020A
MA_NOACTIVATEANDEAT = 4
IDC_ARROW = 32512
HWND_TOPMOST = wintypes.HWND(-1)
SWP_NOACTIVATE = 0x0010
RIDEV_INPUTSINK = 0x00000100
RID_INPUT = 0x10000003
RI_MOUSE_WHEEL = 0x0400


class RAWINPUTDEVICE(ctypes.Structure):
    _fields_ = [
        ("usUsagePage", wintypes.USHORT),
        ("usUsage", wintypes.USHORT),
        ("dwFlags", wintypes.DWORD),
        ("hwndTarget", wintypes.HWND),
    ]


class RAWINPUTHEADER(ctypes.Structure):
    _fields_ = [
        ("dwType", wintypes.DWORD),
        ("dwSize", wintypes.DWORD),
        ("hDevice", wintypes.HANDLE),
        ("wParam", wintypes.WPARAM),
    ]


user32.LoadCursorW.argtypes = [wintypes.HINSTANCE, ctypes.c_void_p]
user32.LoadCursorW.restype = wintypes.HANDLE
user32.SetWindowPos.argtypes = [
    wintypes.HWND,
    wintypes.HWND,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_int,
    wintypes.UINT,
]
user32.SetWindowPos.restype = wintypes.BOOL
user32.RegisterRawInputDevices.argtypes = [ctypes.POINTER(RAWINPUTDEVICE), wintypes.UINT, wintypes.UINT]
user32.RegisterRawInputDevices.restype = wintypes.BOOL
user32.GetRawInputData.argtypes = [
    wintypes.LPARAM,
    wintypes.UINT,
    ctypes.c_void_p,
    ctypes.POINTER(wintypes.UINT),
    wintypes.UINT,
]
user32.GetRawInputData.restype = wintypes.UINT


class MouseInputCatcher:
    """
    - Uses Win32 Raw Input (RIDEV_INPUTSINK) to read Mouse Scroll Wheel events in the background
      while remaining 100% HIDDEN during normal gameplay so MuMu Player's WASD keymapper is never blocked!
    - Only becomes visible when the Minecraft Inventory ('E') is open, so clicking Creative tabs
      and moving items in the Inventory never sends unwanted clicks to Brawl Stars underneath.
    """

    def __init__(self):
        self.hwnd = None
        self.visible = False
        self.last_rect: tuple[int, int, int, int] = (0, 0, 0, 0)
        self._wheel_accum: int = 0
        self._raw_buf = (ctypes.c_ubyte * 64)()
        self._wndproc = WNDPROCTYPE(self._wnd_proc)
        self._create_window()

    def _wnd_proc(self, hwnd, msg, wparam, lparam):
        if msg == WM_INPUT:
            sz = wintypes.UINT(64)
            res = user32.GetRawInputData(
                lparam,
                RID_INPUT,
                ctypes.byref(self._raw_buf),
                ctypes.byref(sz),
                ctypes.sizeof(RAWINPUTHEADER),
            )
            if res != 0xFFFFFFFF and res >= 32:
                raw_bytes = bytes(self._raw_buf[:32])
                dw_type = struct.unpack_from("<I", raw_bytes, 0)[0]
                if dw_type == 0:  # RIM_TYPEMOUSE
                    btn_flags = struct.unpack_from("<H", raw_bytes, 28)[0]
                    if btn_flags & RI_MOUSE_WHEEL:
                        delta = struct.unpack_from("<h", raw_bytes, 30)[0]
                        if delta != 0:
                            self._wheel_accum += delta
            return 0
        if msg == WM_MOUSEACTIVATE:
            return MA_NOACTIVATEANDEAT
        if msg == WM_MOUSEWHEEL:
            delta = ctypes.c_short((wparam >> 16) & 0xFFFF).value
            if delta != 0:
                self._wheel_accum += delta
            return 0
        if msg in (0x0201, 0x0202, 0x0204, 0x0205, 0x0207, 0x0208):
            # When visible (Inventory open), eat mouse clicks so MuMu never gets a touch tap
            return 0
        return user32.DefWindowProcW(hwnd, msg, wparam, lparam)

    def _create_window(self):
        hinst = kernel32.GetModuleHandleW(None)
        cls_name = "BSLMCMouseInputCatcher"
        wc = WNDCLASSEXW()
        wc.cbSize = ctypes.sizeof(WNDCLASSEXW)
        wc.lpfnWndProc = self._wndproc
        wc.hInstance = hinst
        wc.hCursor = user32.LoadCursorW(None, IDC_ARROW)
        wc.lpszClassName = cls_name
        user32.RegisterClassExW(ctypes.byref(wc))

        ex_style = WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
        self.hwnd = user32.CreateWindowExW(
            ex_style,
            cls_name,
            "BSL x Minecraft Input Catcher",
            WS_POPUP,
            100,
            100,
            1280,
            720,
            None,
            None,
            hinst,
            None,
        )
        if self.hwnd:
            user32.SetLayeredWindowAttributes(self.hwnd, 0, 1, LWA_ALPHA)
            rid = RAWINPUTDEVICE()
            rid.usUsagePage = 0x01  # HID_USAGE_PAGE_GENERIC
            rid.usUsage = 0x02      # HID_USAGE_GENERIC_MOUSE
            rid.dwFlags = RIDEV_INPUTSINK
            rid.hwndTarget = self.hwnd
            user32.RegisterRawInputDevices(ctypes.byref(rid), 1, ctypes.sizeof(RAWINPUTDEVICE))

    def update_state(self, active: bool, target_rect: tuple[int, int, int, int] | None):
        if not self.hwnd:
            return
        if not active or target_rect is None:
            if self.visible:
                self.visible = False
                user32.ShowWindow(self.hwnd, SW_HIDE)
            return
        rx, ry, rw, rh = target_rect
        cy = ry + 36
        ch = max(100, rh - 36)
        new_rect = (rx, cy, rw, ch)
        if new_rect != self.last_rect:
            self.last_rect = new_rect
            user32.SetWindowPos(
                self.hwnd,
                HWND_TOPMOST,
                rx,
                cy,
                rw,
                ch,
                SWP_NOACTIVATE,
            )
        if not self.visible:
            self.visible = True
            user32.ShowWindow(self.hwnd, SW_SHOWNOACTIVATE)

    def pop_wheel_steps(self) -> int:
        if abs(self._wheel_accum) < 120:
            return 0
        steps = int(self._wheel_accum // 120)
        self._wheel_accum -= steps * 120
        return steps


class PassthroughOverlay:
    def __init__(self, width: int = 1280, height: int = 720):
        self.width = width
        self.height = height
        self.x = 100
        self.y = 100
        self.hwnd = None
        self.visible = False
        self._wndproc = WNDPROCTYPE(self._wnd_proc)
        self._create_window()

    def _wnd_proc(self, hwnd, msg, wparam, lparam):
        return user32.DefWindowProcW(hwnd, msg, wparam, lparam)

    def _create_window(self):
        hinst = kernel32.GetModuleHandleW(None)
        cls_name = "BSLMCPassthroughOverlay"
        wc = WNDCLASSEXW()
        wc.cbSize = ctypes.sizeof(WNDCLASSEXW)
        wc.lpfnWndProc = self._wndproc
        wc.hInstance = hinst
        wc.lpszClassName = cls_name
        user32.RegisterClassExW(ctypes.byref(wc))

        ex_style = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
        self.hwnd = user32.CreateWindowExW(
            ex_style,
            cls_name,
            "BSL x Minecraft Passthrough Overlay",
            WS_POPUP,  # Created hidden; only shown while in_match is True
            self.x,
            self.y,
            self.width,
            self.height,
            None,
            None,
            hinst,
            None,
        )

    def set_visible(self, show: bool):
        if not self.hwnd or self.visible == show:
            return
        self.visible = show
        user32.ShowWindow(self.hwnd, SW_SHOWNOACTIVATE if show else SW_HIDE)

    def update_frame(self, bgra: np.ndarray, target_rect: tuple[int, int, int, int]):
        if not self.hwnd:
            return
        self.set_visible(True)
        self.x, self.y, self.width, self.height = target_rect

        h, w, _ = bgra.shape
        hdc_screen = user32.GetDC(None)
        hdc_mem = gdi32.CreateCompatibleDC(hdc_screen)

        bmi = BITMAPINFO()
        bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
        bmi.bmiHeader.biWidth = w
        bmi.bmiHeader.biHeight = -h  # Top-down DIB
        bmi.bmiHeader.biPlanes = 1
        bmi.bmiHeader.biBitCount = 32
        bmi.bmiHeader.biCompression = 0

        ppv_bits = ctypes.c_void_p()
        hbmp = gdi32.CreateDIBSection(hdc_mem, ctypes.byref(bmi), DIB_RGB_COLORS, ctypes.byref(ppv_bits), None, 0)
        if hbmp and ppv_bits.value:
            old_bmp = gdi32.SelectObject(hdc_mem, hbmp)
            ctypes.memmove(ppv_bits.value, bgra.ctypes.data, w * h * 4)

            dst_x = self.x + (self.width - w) // 2
            dst_y = self.y + (self.height - h)
            pt_dst = POINT(dst_x, dst_y)
            size = SIZE(w, h)
            pt_src = POINT(0, 0)
            blend = BLENDFUNCTION(AC_SRC_OVER, 0, 255, AC_SRC_ALPHA)

            user32.UpdateLayeredWindow(
                self.hwnd,
                hdc_screen,
                ctypes.byref(pt_dst),
                ctypes.byref(size),
                hdc_mem,
                ctypes.byref(pt_src),
                0,
                ctypes.byref(blend),
                ULW_ALPHA,
            )
            gdi32.SelectObject(hdc_mem, old_bmp)
            gdi32.DeleteObject(hbmp)

        gdi32.DeleteDC(hdc_mem)
        user32.ReleaseDC(None, hdc_screen)

    def pump_messages(self):
        msg = wintypes.MSG()
        while user32.PeekMessageW(ctypes.byref(msg), None, 0, 0, 1):
            user32.TranslateMessage(ctypes.byref(msg))
            user32.DispatchMessageW(ctypes.byref(msg))


# --------------------------------------------------------------------------- Main Passthrough Loop
class BslMinecraftPassthrough:
    HOTBAR_NAMES = (
        "Ender Pearl",
        "Diamond Sword",
        "Multishot Crossbow",
        "Power V Bow",
        "TNT",
        "Flint and Steel",
        "Fire Charge (Ghast Fireball)",
        "Grass Block",
        "Wind Charge",
    )

    def __init__(self, mem_bridge: MumuMemoryBridge, input_catcher: MouseInputCatcher):
        self.mem_bridge = mem_bridge
        self.input_catcher = input_catcher
        self.mc_sock: socket.socket | None = None
        self.bsl_sock: socket.socket | None = None
        self._last_mc_try: float = 0.0
        self._last_bsl_try: float = 0.0
        self.frame_num = 0
        self.ground_sent = False
        self.items_stocked = False
        self.last_view_size: tuple[int, int] = (0, 0)
        self.enabled = True
        self.in_match = False
        self.manual_override: bool | None = None
        self.mouse_look_locked = False
        self.inventory_open = False
        self.running = True
        self._key_hold_start: dict[int, float] = {}
        self._key_last_repeat: dict[int, float] = {}
        self._last_cursor: tuple[int, int] | None = None
        self._last_gui_mouse: tuple[float, float] | None = None
        self.current_slot: int = 1

        # First-Person camera coordinates in Minecraft space (y=64.0 is the Brawl Stars arena floor)
        self.cam_x = 0.0
        self.cam_y = 65.6667  # 64.0 + 500.0 / 300.0 (Steve's eye level)
        self.cam_z = 0.0
        self.cam_yaw = 180.0
        self.cam_pitch = 0.0
        self.fov = 60.0
        self.jump_offset_y = 0.0
        self.jump_vel_y = 0.0
        self.peak_jump_y = 0.0

        # User's Minecraft settings & keybinds (synced from options.txt and live Minecraft Options)
        self.mc_sensitivity: float = 0.5
        self.vk_attack: int = 0x01  # Default Left Mouse Button (or user's custom Attack button/key!)
        self.vk_use: int = 0x02     # Default Right Mouse Button (or user's custom Use button/key!)
        self.vk_pick: int = 0x04    # Default Middle Mouse Button
        self.vk_jump: int = 0x20    # Default Space
        self.vk_inventory: int = ord("E")
        self.vk_chat: int = ord("T")
        self.vk_command: int = 0xBF  # /
        self.vk_sneak: int = 0x10   # Shift
        self.vk_sprint: int = 0x11  # Ctrl
        self.vk_swap_offhand: int = ord("F")
        self.vk_drop: int = ord("Q")  # Default Q (or user's drop binding)
        self.vk_hotbar: list[int] = [0x31 + i for i in range(9)]
        self._load_minecraft_options()

        self.brawlers: list[tuple[int, float, float, int, int, int, int]] = []
        self._last_hit_time: dict[int, float] = {}
        self._atk_down_pos: tuple[int, int] | None = None
        self._atk_down_time: float = 0.0
        self._atk_is_drag: bool = False
        self._atk_sent: bool = False
        self._use_down_sent: bool = False
        self._teleport_lock_until: float = 0.0
        self._last_tnt_pos: tuple[int, int, int] | None = None

        self.key_state: dict[int, bool] = {}

    @staticmethod
    def _mc_key_to_vk(key_str: str) -> int | None:
        key_str = key_str.strip().lower()
        mouse_map = {
            "key.mouse.left": 0x01,    # VK_LBUTTON
            "key.mouse.right": 0x02,   # VK_RBUTTON
            "key.mouse.middle": 0x04,  # VK_MBUTTON
            "key.mouse.0": 0x01,
            "key.mouse.1": 0x02,
            "key.mouse.2": 0x04,
            "key.mouse.3": 0x04,
            "key.mouse.4": 0x05,       # VK_XBUTTON1
            "key.mouse.5": 0x06,       # VK_XBUTTON2
            "key.mouse.6": 0x07,
            "key.mouse.7": 0x08,
        }
        if key_str in mouse_map:
            return mouse_map[key_str]
        if not key_str.startswith("key.keyboard."):
            return None
        k = key_str[len("key.keyboard."):]
        if len(k) == 1 and "a" <= k <= "z":
            return ord(k.upper())
        if len(k) == 1 and "0" <= k <= "9":
            return ord(k)
        if k.startswith("f") and k[1:].isdigit():
            fn = int(k[1:])
            if 1 <= fn <= 12:
                return 0x70 + (fn - 1)
        if k.startswith("numpad.") and len(k) == 8 and k[-1].isdigit():
            return 0x60 + int(k[-1])
        special = {
            "tab": 0x09,
            "space": 0x20,
            "left.shift": 0x10,
            "right.shift": 0x10,
            "left.control": 0x11,
            "right.control": 0x11,
            "left.alt": 0x12,
            "right.alt": 0x12,
            "caps.lock": 0x14,
            "grave.accent": 0xC0,
            "slash": 0xBF,
            "backslash": 0xDC,
            "minus": 0xBD,
            "equal": 0xBB,
            "left.bracket": 0xDB,
            "right.bracket": 0xDD,
            "semicolon": 0xBA,
            "apostrophe": 0xDE,
            "comma": 0xBC,
            "period": 0xBE,
            "enter": 0x0D,
            "backspace": 0x08,
            "delete": 0x2E,
            "insert": 0x2D,
            "home": 0x24,
            "end": 0x23,
            "page.up": 0x21,
            "page.down": 0x22,
            "up": 0x26,
            "down": 0x28,
            "left": 0x25,
            "right": 0x27,
            "num.lock": 0x90,
            "scroll.lock": 0x91,
            "pause": 0x13,
            "print.screen": 0x2C,
            "escape": 0x1B,
        }
        return special.get(k)

    @staticmethod
    def _vk_label(vk: int) -> str:
        names = {
            0x01: "Left Click",
            0x02: "Right Click",
            0x04: "Middle Click",
            0x05: "Mouse 4",
            0x06: "Mouse 5",
            0x09: "Tab",
            0x10: "Shift",
            0x11: "Ctrl",
            0x20: "Space",
            0x2E: "Delete",
            0x08: "Backspace",
        }
        if vk in names:
            return names[vk]
        if 0x30 <= vk <= 0x5A:
            return chr(vk)
        return f"0x{vk:02X}"

    def _load_minecraft_options(self):
        """Load the user's personal sensitivity and keybinds from the most recently active Modrinth / .minecraft options.txt."""
        appdata = os.environ.get("APPDATA", "")
        candidates: list[str] = [
            os.path.join(appdata, "ModrinthApp", "profiles", "Fabric 26.3", "options.txt"),
        ]
        profiles_dir = os.path.join(appdata, "ModrinthApp", "profiles")
        if os.path.isdir(profiles_dir):
            try:
                for entry in os.listdir(profiles_dir):
                    opt_p = os.path.join(profiles_dir, entry, "options.txt")
                    if os.path.isfile(opt_p) and opt_p not in candidates:
                        candidates.append(opt_p)
            except Exception:
                pass
        candidates.append(os.path.join(appdata, ".minecraft", "options.txt"))

        valid = [p for p in candidates if os.path.isfile(p)]
        valid.sort(key=lambda p: os.path.getmtime(p), reverse=True)

        for path in valid:
            try:
                loaded = False
                with open(path, "r", encoding="utf-8", errors="ignore") as f:
                    for raw_line in f:
                        line = raw_line.strip()
                        if ":" not in line:
                            continue
                        k, v = line.split(":", 1)
                        if k == "mouseSensitivity":
                            try:
                                self.mc_sensitivity = max(0.05, min(2.0, float(v)))
                                loaded = True
                            except ValueError:
                                pass
                        elif k == "key_key.attack":
                            vk = self._mc_key_to_vk(v)
                            if vk is not None:
                                self.vk_attack = vk
                                loaded = True
                        elif k == "key_key.use":
                            vk = self._mc_key_to_vk(v)
                            if vk is not None:
                                self.vk_use = vk
                                loaded = True
                        elif k == "key_key.pickItem":
                            vk = self._mc_key_to_vk(v)
                            if vk is not None:
                                self.vk_pick = vk
                        elif k == "key_key.jump":
                            vk = self._mc_key_to_vk(v)
                            if vk is not None:
                                self.vk_jump = vk
                        elif k == "key_key.inventory":
                            vk = self._mc_key_to_vk(v)
                            if vk is not None:
                                self.vk_inventory = vk
                        elif k == "key_key.chat":
                            vk = self._mc_key_to_vk(v)
                            if vk is not None:
                                self.vk_chat = vk
                        elif k == "key_key.command":
                            vk = self._mc_key_to_vk(v)
                            if vk is not None:
                                self.vk_command = vk
                        elif k == "key_key.sneak":
                            vk = self._mc_key_to_vk(v)
                            if vk is not None:
                                self.vk_sneak = vk
                        elif k == "key_key.sprint":
                            vk = self._mc_key_to_vk(v)
                            if vk is not None:
                                self.vk_sprint = vk
                        elif k == "key_key.swapOffhand":
                            vk = self._mc_key_to_vk(v)
                            if vk is not None:
                                self.vk_swap_offhand = vk
                        elif k == "key_key.drop":
                            vk = self._mc_key_to_vk(v)
                            if vk is not None:
                                self.vk_drop = vk
                        elif k.startswith("key_key.hotbar."):
                            try:
                                idx = int(k.split(".")[-1]) - 1
                                vk = self._mc_key_to_vk(v)
                                if 0 <= idx < 9 and vk is not None:
                                    self.vk_hotbar[idx] = vk
                            except ValueError:
                                pass
                if loaded:
                    print(f"[Passthrough] Loaded Minecraft options from: {path}")
                    print(
                        f"[Passthrough] Active Keybinds: Attack={self._vk_label(self.vk_attack)}, "
                        f"Use={self._vk_label(self.vk_use)}, Inventory={self._vk_label(self.vk_inventory)}, "
                        f"Pick={self._vk_label(self.vk_pick)}, Jump={self._vk_label(self.vk_jump)}"
                    )
                    break
            except Exception:
                pass

    def select_hotbar_slot(self, slot_idx: int):
        slot_idx = max(0, min(8, int(slot_idx)))
        self.current_slot = slot_idx
        self.send_mc({"t": "slot", "n": slot_idx})
        if not self.inventory_open:
            item_name = self.HOTBAR_NAMES[slot_idx] if 0 <= slot_idx < len(self.HOTBAR_NAMES) else f"Slot {slot_idx + 1}"
            print(f"[Passthrough] Switched to Hotbar Slot {slot_idx + 1}: {item_name}")

    def scroll_hotbar(self, delta: int):
        self.current_slot = (self.current_slot - delta) % 9
        self.send_mc({"t": "scroll", "d": delta})
        self.send_mc({"t": "slot", "n": self.current_slot})
        item_name = self.HOTBAR_NAMES[self.current_slot]
        print(f"[Passthrough] Switched to Hotbar Slot {self.current_slot + 1}: {item_name}")

    def connect_mc(self) -> bool:
        if self.mc_sock is not None:
            return True
        now = time.perf_counter()
        if now - self._last_mc_try < 1.5:
            return False
        self._last_mc_try = now
        try:
            s = socket.create_connection(("127.0.0.1", 25599), timeout=0.05)
            s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            s.setblocking(False)
            self.mc_sock = s
            self.ground_sent = False
            self.last_view_size = (0, 0)
            print("[Passthrough] Connected to Minecraft HostLink on 127.0.0.1:25599!")
            self.send_mc({"t": "cmd", "c": "gamemode creative @a"})
            hide_and_unthrottle_minecraft_window()
            if not self.in_match:
                self.send_mc({"t": "idle"})
                print("[Passthrough] No Brawl Stars match active yet -> Minecraft screen is BLACK.")
            return True
        except OSError:
            return False

    def connect_bsl(self) -> bool:
        if self.bsl_sock is not None:
            return True
        now = time.perf_counter()
        if now - self._last_bsl_try < 1.5:
            return False
        self._last_bsl_try = now
        try:
            s = socket.create_connection(("127.0.0.1", 8080), timeout=0.05)
            s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            s.setblocking(False)
            self.bsl_sock = s
            print("[Passthrough] Connected to CustomBrawlServer Bridge on 127.0.0.1:8080!")
            return True
        except OSError:
            return False

    def send_mc(self, obj: dict) -> bool:
        if not self.connect_mc():
            return False
        try:
            line = (json.dumps(obj, separators=(",", ":")) + "\n").encode("utf-8")
            self.mc_sock.sendall(line)
            return True
        except OSError:
            self.mc_sock = None
            return False

    def send_bsl(self, obj: dict):
        if self.connect_bsl():
            try:
                line = (json.dumps(obj, separators=(",", ":")) + "\n").encode("utf-8")
                self.bsl_sock.sendall(line)
            except OSError:
                self.bsl_sock = None

    def set_in_match(self, active: bool, reason: str = ""):
        if self.in_match == active:
            return
        self.in_match = active
        if active:
            print(f"[Passthrough] >>> MATCH STARTED ({reason})! First-Person Steve POV + Free Mouse-Look ACTIVE!")
            self._load_minecraft_options()
            self.mouse_look_locked = True
            self.inventory_open = False
            self.jump_offset_y = 0.0
            self.jump_vel_y = 0.0
            self.peak_jump_y = 0.0
            self._last_cursor = None
            self.input_catcher.update_state(False, None)
            mumu = find_mumu_window()
            if mumu is not None:
                m_hwnd, (rx, ry, rw, rh) = mumu
                try:
                    user32.SetForegroundWindow(m_hwnd)
                    user32.SetCursorPos(rx + rw // 2, ry + rh // 2)
                except Exception:
                    pass
            self.ground_sent = False
            self.send_arena_ground(restock_items=not self.items_stocked)
        else:
            print(f"[Passthrough] <<< MATCH ENDED ({reason})! Minecraft is now showing a BLACK SCREEN.")
            self.mouse_look_locked = False
            self.inventory_open = False
            self.jump_offset_y = 0.0
            self.jump_vel_y = 0.0
            self.peak_jump_y = 0.0
            self._last_cursor = None
            self.input_catcher.update_state(False, None)
            self.brawlers = []
            self.send_mc({"t": "peds", "p": []})
            self.send_mc({"t": "idle"})

    def send_arena_ground(self, restock_items: bool = False):
        """Fill y=60..63 with invisible barrier blocks across the Brawl Stars arena and optionally stock starter items."""
        if not self.connect_mc():
            self.ground_sent = False
            return
        self.send_mc({"t": "cmd", "c": "gamerule commandModificationBlockLimit 2000000"})
        self.send_mc({"t": "cmd", "c": "gamemode creative @a"})
        # 100% unconditional sky purge: remove ALL stray blocks, floating clutter, and barriers above the floor (y=64..128)
        self.send_mc({"t": "cmd", "c": "fill -64 64 -64 64 74 64 minecraft:air"})
        self.send_mc({"t": "cmd", "c": "fill -64 75 -64 64 86 64 minecraft:air"})
        self.send_mc({"t": "cmd", "c": "fill -64 87 -64 64 128 64 minecraft:air"})
        self.send_mc({"t": "cmd", "c": "fill -64 60 -64 64 63 64 minecraft:barrier replace air"})
        self.send_mc({"t": "cmd", "c": "attribute @a minecraft:block_interaction_range base set 12.0"})
        self.send_mc({"t": "cmd", "c": "attribute @a minecraft:entity_interaction_range base set 8.0"})
        for z0 in range(-36, 37, 12):
            cols = []
            z1 = min(36, z0 + 11)
            for x in range(-32, 33):
                for z in range(z0, z1 + 1):
                    cols.extend([x, z, 61, 63])
            if not self.send_mc({"t": "ground", "c": cols}):
                self.ground_sent = False
                return
        self.send_mc({"t": "cmd", "c": "time set noon"})
        self.send_mc({"t": "cmd", "c": "weather clear"})
        if restock_items or not self.items_stocked:
            for cmd in (
                # Hotbar (Slots 1-9, no Creeper Spawn Egg!)
                "item replace entity @a hotbar.0 with minecraft:ender_pearl 16",
                "item replace entity @a hotbar.1 with minecraft:diamond_sword[enchantments={sharpness:5}]",
                "item replace entity @a hotbar.2 with minecraft:crossbow[enchantments={multishot:1,quick_charge:3}]",
                "item replace entity @a hotbar.3 with minecraft:bow[enchantments={power:5,flame:1,infinity:1}]",
                "item replace entity @a hotbar.4 with minecraft:tnt 64",
                "item replace entity @a hotbar.5 with minecraft:flint_and_steel",
                "item replace entity @a hotbar.6 with minecraft:fire_charge 64",
                "item replace entity @a hotbar.7 with minecraft:grass_block 64",
                "item replace entity @a hotbar.8 with minecraft:wind_charge 64",
                "item replace entity @a weapon.offhand with minecraft:firework_rocket 64",
                # Extra working items in Inventory (press E to open and equip!)
                "item replace entity @a inventory.0 with minecraft:trident[enchantments={loyalty:3,impaling:5}]",
                "item replace entity @a inventory.1 with minecraft:mace[enchantments={density:5,wind_burst:3}]",
                "item replace entity @a inventory.2 with minecraft:snowball 16",
                "item replace entity @a inventory.3 with minecraft:egg 16",
                "item replace entity @a inventory.4 with minecraft:splash_potion[potion_contents={potion:\"minecraft:strong_harming\"}] 16",
                "item replace entity @a inventory.5 with minecraft:lingering_potion[potion_contents={potion:\"minecraft:strong_poison\"}] 16",
                "item replace entity @a inventory.6 with minecraft:enchanted_golden_apple 64",
                "item replace entity @a inventory.7 with minecraft:oak_planks 64",
                "item replace entity @a inventory.8 with minecraft:cobblestone 64",
                "item replace entity @a inventory.9 with minecraft:obsidian 64",
                "item replace entity @a inventory.10 with minecraft:glowstone 64",
                "item replace entity @a inventory.11 with minecraft:cobweb 64",
                "item replace entity @a inventory.12 with minecraft:lava_bucket",
                "item replace entity @a inventory.13 with minecraft:water_bucket",
                "item replace entity @a inventory.14 with minecraft:totem_of_undying",
                "item replace entity @a inventory.15 with minecraft:arrow 64",
                "item replace entity @a inventory.16 with minecraft:end_crystal 64",
                "item replace entity @a inventory.17 with minecraft:respawn_anchor 64",
                "item replace entity @a inventory.18 with minecraft:shield",
                "item replace entity @a inventory.19 with minecraft:fishing_rod",
                "item replace entity @a inventory.20 with minecraft:chorus_fruit 64",
                "item replace entity @a inventory.21 with minecraft:anvil 64",
            ):
                self.send_mc({"t": "cmd", "c": cmd})
            self.items_stocked = True
            self.select_hotbar_slot(self.current_slot)
        if self.mc_sock is not None:
            self.ground_sent = True
            print(
                f"[Passthrough] Sent Brawl Stars arena floor + stocked Hotbar & Inventory "
                f"(Attack={self._vk_label(self.vk_attack)}, Use={self._vk_label(self.vk_use)}, E=Inventory, F10=Options)."
            )

    def _reinforce_local_ground(self):
        """Ensure solid barrier blocks (y=61..63) exist around Steve even if the Minecraft world loaded after match start."""
        cx = int(round(self.cam_x))
        cz = int(round(self.cam_z))
        cols = []
        for x in range(cx - 7, cx + 8):
            for z in range(cz - 7, cz + 8):
                cols.extend([x, z, 61, 63])
        self.send_mc({"t": "ground", "c": cols})
        self.send_mc({"t": "cmd", "c": f"fill {cx - 14} 60 {cz - 14} {cx + 14} 63 {cz + 14} minecraft:barrier replace air"})

    def sync_from_memory_bridge(self):
        """Sync exact match state, 1:1 First-Person camera, jump/wind-charge vertical physics, and enemy brawler list."""
        connected, in_battle, eye, target, tp_yaw, tp_pitch, fov, brawlers = self.mem_bridge.snapshot()
        if connected and self.manual_override is None:
            if in_battle != self.in_match:
                self.set_in_match(in_battle, "BattleScreen::s_pInstance memory check")

        # Step vertical jump / Wind Charge rocket-jump physics at 60Hz
        if self.jump_offset_y > 0.0 or self.jump_vel_y != 0.0:
            self.jump_offset_y += self.jump_vel_y
            self.jump_vel_y -= 0.020  # Floatier, authentic Minecraft jump/launch gravity at 60Hz
            if self.jump_offset_y > self.peak_jump_y:
                self.peak_jump_y = self.jump_offset_y
            if self.jump_offset_y <= 0.0:
                self.jump_offset_y = 0.0
                self.jump_vel_y = 0.0
                self.peak_jump_y = 0.0

        if in_battle:
            self.brawlers = brawlers
            eye_x, eye_y, eye_z = eye
            target_x, target_y, target_z = target
            raw_x = (eye_x - 3150.0) / 300.0
            raw_y = 64.0 + (eye_z / 300.0) + self.jump_offset_y
            raw_z = (-eye_y - 4950.0) / 300.0

            if time.perf_counter() >= self._teleport_lock_until or math.hypot(raw_x - self.cam_x, raw_z - self.cam_z) < 0.6:
                self._teleport_lock_until = 0.0
                self.cam_x, self.cam_y, self.cam_z = raw_x, raw_y, raw_z
            else:
                self.cam_y = raw_y

            dx = (target_x - eye_x) / 300.0
            dy = (target_z - eye_z) / 300.0
            dz = (-(target_y - eye_y)) / 300.0
            horiz = math.hypot(dx, dz)
            if horiz > 1e-5 or abs(dy) > 1e-5:
                self.cam_yaw = math.degrees(math.atan2(-dx, dz))
                self.cam_pitch = math.degrees(math.atan2(-dy, max(1e-6, horiz)))
            self.fov = 60.0

    def poll_bsl_events(self):
        if not self.connect_bsl():
            return
        try:
            data = self.bsl_sock.recv(8192)
            if not data:
                self.bsl_sock = None
                return
            for line in data.decode("utf-8", errors="ignore").splitlines():
                line = line.strip()
                if not line.startswith("{"):
                    continue
                msg = json.loads(line)
                ev = msg.get("event")
                mem_connected, _, _, _, _, _, _, _ = self.mem_bridge.snapshot()
                if ev == "match_state" and not mem_connected:
                    st = msg.get("state")
                    if st == "start":
                        self.set_in_match(True, "BSL match_state=start")
                    elif st == "end":
                        self.set_in_match(False, "BSL match_state=end")
                elif ev == "character_state" and not mem_connected:
                    action = msg.get("action", "")
                    if "inBattle" in msg:
                        if msg["inBattle"] and action == "match_start":
                            self.set_in_match(True, "BSL match_start")
                        elif not msg["inBattle"] and action in ("match_end", "login", "disconnect"):
                            self.set_in_match(False, f"BSL {action}")
                    bx = msg.get("x", 3150)
                    by = msg.get("y", 4950)
                    self.cam_x = (bx - 3150.0) / 300.0
                    self.cam_z = (by - 4950.0) / 300.0
        except BlockingIOError:
            pass
        except Exception:
            pass

    def _apply_hit_to_brawler(
        self,
        handle: int,
        mc_dmg: float,
        src_x: float,
        src_z: float,
        b_mc_x: float,
        b_mc_z: float,
        label: str,
        knock_units: int = 220,
    ):
        now = time.perf_counter()
        if now - self._last_hit_time.get(handle, 0.0) < 0.12:
            return
        self._last_hit_time[handle] = now
        bsl_dmg = int(max(1.0, mc_dmg) * 250.0)
        dx = b_mc_x - src_x
        dz = b_mc_z - src_z
        dist = max(0.01, math.hypot(dx, dz))
        kx = int((dx / dist) * knock_units)
        ky = int((dz / dist) * knock_units)
        self.mem_bridge.queue_brawler_damage(handle, bsl_dmg, kx, ky)
        self.send_bsl({"event": "mc_damage", "playerId": handle, "damage": bsl_dmg})
        print(f"[Passthrough] Hit Brawler #{handle} ({label}) -> dealt {bsl_dmg} Brawl Stars damage!")

    def _perform_melee_sweep(self, item_id: str = "diamond_sword"):
        """Wide melee cone sweep in front of Steve's camera so sword/mace/trident attacks reliably hit Brawlers."""
        base_dmg = 7.0
        knock = 220
        label = "Diamond Sword"
        is_mace = "mace" in item_id
        if is_mace:
            fall_bonus = max(0.0, self.peak_jump_y) * 4.5
            base_dmg = 14.0 + fall_bonus
            knock = 460 if fall_bonus > 2.0 else 340
            label = f"Mace Smash (+{fall_bonus:.0f} fall)" if fall_bonus >= 1.2 else "Mace Smash"
        elif "trident" in item_id:
            base_dmg = 9.0
            knock = 260
            label = "Trident"
        elif "sword" not in item_id and "axe" not in item_id:
            base_dmg = 4.0
            label = f"Melee ({item_id})"

        yaw_rad = math.radians(self.cam_yaw)
        look_x = -math.sin(yaw_rad)
        look_z = math.cos(yaw_rad)
        hit_any = False
        for h_id, mx, mz, bhp, _, _, _ in self.brawlers:
            dx = mx - self.cam_x
            dz = mz - self.cam_z
            dist = math.hypot(dx, dz)
            if 0.01 < dist <= 5.5:
                dot = (dx * look_x + dz * look_z) / dist
                if dot >= 0.35 or dist <= 1.6:
                    self._apply_hit_to_brawler(h_id, base_dmg, self.cam_x, self.cam_z, mx, mz, label, knock_units=knock)
                    hit_any = True
        # Mace Wind Burst III enchantment: smashing an enemy while falling launches Steve high back up into the air!
        if is_mace and hit_any and self.peak_jump_y >= 0.8:
            self.jump_vel_y = max(self.jump_vel_y + 0.95, 1.25)
            self.peak_jump_y = 0.0
            print("[Passthrough] Mace Wind Burst III -> Launched Steve high back into the air!")

    def _apply_wind_charge_burst(self, ex: float, ez: float):
        """Apply authentic Minecraft Wind Charge rocket-jump & horizontal knockback to Steve + enemy Brawlers."""
        dist_self = math.hypot(self.cam_x - ex, self.cam_z - ez)
        if dist_self <= 4.8:
            # Launch Steve vertically high into the air (rocket-jump!)
            self.jump_vel_y = max(self.jump_vel_y + 0.85, 1.25)
            # Push Steve horizontally in Brawl Stars: if looking down at feet, boost forward along look direction!
            yaw_rad = math.radians(self.cam_yaw)
            look_x = -math.sin(yaw_rad)
            look_z = math.cos(yaw_rad)
            if dist_self < 1.6 or self.cam_pitch > 30.0:
                push_mc_x = self.cam_x + look_x * 2.6
                push_mc_z = self.cam_z + look_z * 2.6
            else:
                dx = (self.cam_x - ex) / max(0.2, dist_self)
                dz = (self.cam_z - ez) / max(0.2, dist_self)
                push_mc_x = self.cam_x + dx * 2.4
                push_mc_z = self.cam_z + dz * 2.4
            new_bx = max(300, min(6000, int(round(push_mc_x * 300.0 + 3150.0))))
            new_by = max(300, min(9600, int(round(push_mc_z * 300.0 + 4950.0))))
            self.cam_x = (new_bx - 3150.0) / 300.0
            self.cam_z = (new_by - 4950.0) / 300.0
            self._teleport_lock_until = time.perf_counter() + 0.50
            self.mem_bridge.queue_player_teleport(new_bx, new_by)
            print(f"[Passthrough] Wind Charge Burst -> Rocket-Jumped Steve to ({self.cam_x:.1f}, {self.cam_z:.1f})!")

        for h_id, mx, mz, bhp, _, _, _ in self.brawlers:
            dist = math.hypot(mx - ex, mz - ez)
            if dist <= 4.8:
                self._apply_hit_to_brawler(
                    h_id,
                    3.5,
                    ex,
                    ez,
                    mx,
                    mz,
                    "Wind Charge Burst",
                    knock_units=520,
                )

    def _handle_right_click_assist(self, used_item: str):
        """Ensure TNT placement and Flint & Steel ignition work seamlessly when holding TNT or Flint & Steel."""
        yaw_rad = math.radians(self.cam_yaw)
        look_x = -math.sin(yaw_rad)
        look_z = math.cos(yaw_rad)
        tx = int(math.floor(self.cam_x + look_x * 2.5))
        tz = int(math.floor(self.cam_z + look_z * 2.5))

        if used_item == "tnt":
            # Place TNT block on the floor in front of Steve even when looking horizontally
            self._last_tnt_pos = (tx, 64, tz)
            if self.cam_pitch < 20.0:
                self.send_mc({"t": "cmd", "c": f"setblock {tx} 64 {tz} minecraft:tnt keep"})
        elif used_item == "flint_and_steel":
            # Ignite any placed TNT block in front of Steve or at the last placed TNT position
            candidates = {(tx, 64, tz)}
            for ox in (-1, 0, 1):
                for oz in (-1, 0, 1):
                    candidates.add((tx + ox, 64, tz + oz))
            if self._last_tnt_pos is not None:
                lx, ly, lz = self._last_tnt_pos
                if math.hypot(lx - self.cam_x, lz - self.cam_z) <= 8.0:
                    candidates.add((lx, ly, lz))
            for bx, by, bz in candidates:
                self.send_mc(
                    {
                        "t": "cmd",
                        "c": f"execute if block {bx} {by} {bz} minecraft:tnt run summon minecraft:tnt {bx + 0.5} {by} {bz + 0.5} {{fuse:30}}",
                    }
                )
                self.send_mc(
                    {
                        "t": "cmd",
                        "c": f"execute if block {bx} {by} {bz} minecraft:tnt run setblock {bx} {by} {bz} minecraft:air",
                    }
                )

    def poll_mc_events(self):
        global _mc_java_pid
        if not self.connect_mc():
            return
        try:
            data = self.mc_sock.recv(8192)
            if not data:
                self.mc_sock = None
                return
            for line in data.decode("utf-8", errors="ignore").splitlines():
                line = line.strip()
                if not line.startswith("{"):
                    continue
                msg = json.loads(line)
                t = msg.get("t")
                if t == "hello":
                    pid_val = int(msg.get("pid", 0))
                    if pid_val > 0:
                        _mc_java_pid = pid_val
                    hide_and_unthrottle_minecraft_window()
                elif t == "options":
                    self._load_minecraft_options()
                    if "sens" in msg:
                        self.mc_sensitivity = max(0.05, min(2.0, float(msg.get("sens", 0.5))))
                    for field, attr in (
                        ("attack", "vk_attack"),
                        ("use", "vk_use"),
                        ("pick", "vk_pick"),
                        ("inv", "vk_inventory"),
                        ("chat", "vk_chat"),
                        ("cmd", "vk_command"),
                        ("sneak", "vk_sneak"),
                        ("sprint", "vk_sprint"),
                        ("swap", "vk_swap_offhand"),
                        ("drop", "vk_drop"),
                        ("jump", "vk_jump"),
                    ):
                        if field in msg:
                            vk = self._mc_key_to_vk(str(msg[field]))
                            if vk is not None:
                                setattr(self, attr, vk)
                    print(
                        f"[Passthrough] Synced Minecraft Options: sensitivity={self.mc_sensitivity:.2f}, "
                        f"Attack={self._vk_label(self.vk_attack)}, Use={self._vk_label(self.vk_use)}"
                    )
                elif t == "screen":
                    self.inventory_open = bool(msg.get("open", False))
                    self._last_cursor = None
                    self._last_gui_mouse = None
                    if not self.inventory_open:
                        self.input_catcher.update_state(False, None)
                        self._load_minecraft_options()
                    print(f"[Passthrough] Minecraft GUI Screen open: {self.inventory_open}")
                elif t == "mobhit":
                    if not self.in_match:
                        continue
                    handle = int(msg.get("h", -1))
                    dmg = float(msg.get("d", 7.0))
                    src = msg.get("from", [self.cam_x, self.cam_y, self.cam_z])
                    kind = msg.get("k", "player")
                    if kind == "env" or (kind == "melee" and dmg <= 1.5):
                        continue
                    b_x = None
                    b_z = None
                    for h_id, mx, mz, _, _, _, _ in self.brawlers:
                        if h_id == handle:
                            b_x, b_z = mx, mz
                            break
                    if b_x is not None and b_z is not None:
                        self._apply_hit_to_brawler(handle, dmg, float(src[0]), float(src[2]), b_x, b_z, f"Minecraft {kind}")
                elif t == "melee":
                    item_id = str(msg.get("item", "diamond_sword"))
                    self._perform_melee_sweep(item_id)
                elif t == "use_item":
                    item_id = str(msg.get("item", ""))
                    if item_id in ("tnt", "flint_and_steel"):
                        self._handle_right_click_assist(item_id)
                elif t == "fish_pull":
                    # Fishing Rod hook/reel: pull the nearest enemy Brawler in front of Steve toward Steve!
                    yaw_rad = math.radians(self.cam_yaw)
                    look_x = -math.sin(yaw_rad)
                    look_z = math.cos(yaw_rad)
                    best_h = None
                    best_dist = 999.0
                    best_mx, best_mz = 0.0, 0.0
                    for h_id, mx, mz, bhp, _, _, _ in self.brawlers:
                        dx = mx - self.cam_x
                        dz = mz - self.cam_z
                        dist = math.hypot(dx, dz)
                        if 0.5 < dist <= 12.0:
                            dot = (dx * look_x + dz * look_z) / dist
                            if dot >= 0.45 and dist < best_dist:
                                best_dist = dist
                                best_h = h_id
                                best_mx, best_mz = mx, mz
                    if best_h is not None:
                        self._apply_hit_to_brawler(
                            best_h, 2.0, self.cam_x, self.cam_z, best_mx, best_mz, "Fishing Rod Pull", knock_units=-520
                        )
                elif t == "anvil_drop":
                    pos = msg.get("pos", [self.cam_x, 64, self.cam_z])
                    ax, az = float(pos[0]), float(pos[2])
                    exp_bx = max(300, min(6000, int(round(ax * 300.0 + 3150.0))))
                    exp_by = max(300, min(9600, int(round(az * 300.0 + 4950.0))))
                    self.mem_bridge.queue_brawl_explosion(exp_bx, exp_by, radius_tiles=2)
                    for h_id, mx, mz, bhp, _, _, _ in self.brawlers:
                        if math.hypot(mx - ax, mz - az) <= 2.8:
                            self._apply_hit_to_brawler(h_id, 16.0, ax, az, mx, mz, "Falling Anvil Crush", knock_units=320)
                elif t == "proj":
                    # Check in-flight arrows, tridents, fireworks, fireballs, wind charges, potions, snowballs, eggs
                    for item in msg.get("p", []):
                        if len(item) >= 5:
                            pid = int(item[0])
                            pkind = str(item[1])
                            px, py, pz = float(item[2]), float(item[3]), float(item[4])
                            for h_id, mx, mz, bhp, _, _, _ in self.brawlers:
                                if math.hypot(mx - px, mz - pz) <= 1.85 and 62.0 <= py <= 69.5:
                                    dmg = 9.0
                                    knock = 220
                                    if pkind == "fireball":
                                        dmg = 14.0
                                        knock = 320
                                        exp_bx = max(300, min(6000, int(round(px * 300.0 + 3150.0))))
                                        exp_by = max(300, min(9600, int(round(pz * 300.0 + 4950.0))))
                                        self.mem_bridge.queue_brawl_explosion(exp_bx, exp_by, radius_tiles=3)
                                    elif pkind == "trident":
                                        dmg = 11.0
                                        knock = 260
                                    elif pkind == "potion":
                                        dmg = 12.0
                                    elif pkind == "wind_charge":
                                        dmg = 3.5
                                        knock = 520
                                    elif pkind == "throwable":
                                        dmg = 3.0
                                        knock = 280
                                    self._apply_hit_to_brawler(
                                        h_id, dmg, self.cam_x, self.cam_z, mx, mz, f"Projectile ({pkind})", knock_units=knock
                                    )
                                    # Only send projhit for arrows/fireworks/snowballs/eggs; NEVER discard wind_charge, trident, potion, or fireball!
                                    if pkind in ("arrow", "firework", "throwable"):
                                        self.send_mc({"t": "projhit", "id": pid, "pos": [px, py, pz], "stick": False})
                                    break
                elif t == "explosion":
                    pos = msg.get("pos", [0, 64, 0])
                    radius = float(msg.get("r", 4.0))
                    ex, ez = float(pos[0]), float(pos[2])
                    # Radius <= 2.2 in Minecraft is a Wind Charge burst (radius 1.2) or Mace Wind Burst!
                    if radius <= 2.2:
                        self._apply_wind_charge_burst(ex, ez)
                        continue
                    # Trigger a real Brawl Stars explosion that destroys walls, bushes, and plays explosion VFX/SFX!
                    exp_bx = max(300, min(6000, int(round(ex * 300.0 + 3150.0))))
                    exp_by = max(300, min(9600, int(round(ez * 300.0 + 4950.0))))
                    tiles_r = max(3, min(6, int(round(radius))))
                    self.mem_bridge.queue_brawl_explosion(exp_bx, exp_by, radius_tiles=tiles_r)
                    exp_label = "End Crystal" if radius >= 5.5 else ("Respawn Anchor" if radius >= 4.5 else "TNT/Fireball")
                    base_exp_dmg = 26.0 if radius >= 5.5 else (22.0 if radius >= 4.5 else 18.0)
                    exp_knock = 480 if radius >= 4.5 else 360
                    print(
                        f"[Passthrough] {exp_label} exploded at MC ({ex:.1f}, {ez:.1f}) -> Destroyed Brawl Stars walls & bushes at ({exp_bx}, {exp_by}, r={tiles_r} tiles)!"
                    )
                    blast_r = max(3.5, radius * 2.0)
                    for h_id, mx, mz, bhp, _, _, _ in self.brawlers:
                        dist = math.hypot(mx - ex, mz - ez)
                        if dist <= blast_r:
                            falloff = max(0.25, 1.0 - (dist / (blast_r + 0.5)))
                            self._apply_hit_to_brawler(
                                h_id,
                                base_exp_dmg * falloff,
                                ex,
                                ez,
                                mx,
                                mz,
                                f"{exp_label} Explosion",
                                knock_units=exp_knock,
                            )
                elif t in ("pteleport", "pearl"):
                    pos = msg.get("pos", [0, 64, 0])
                    mc_x = float(pos[0])
                    mc_z = float(pos[2])
                    if abs(mc_x) <= 30.0 and abs(mc_z) <= 36.0:
                        new_bx = max(300, min(6000, int(round(mc_x * 300.0 + 3150.0))))
                        new_by = max(300, min(9600, int(round(mc_z * 300.0 + 4950.0))))
                        self.cam_x = (new_bx - 3150.0) / 300.0
                        self.cam_z = (new_by - 4950.0) / 300.0
                        self._teleport_lock_until = time.perf_counter() + 0.75
                        self.mem_bridge.queue_player_teleport(new_bx, new_by)
                        print(
                            f"[Passthrough] Instant Teleport -> MC ({self.cam_x:.1f}, {self.cam_z:.1f}) / Brawl ({new_bx}, {new_by})!"
                        )
        except BlockingIOError:
            pass
        except Exception as exc:
            print(f"[Passthrough] poll_mc_events warning: {exc}")

    def poll_inputs(self, mumu_hwnd: int | None, target_rect: tuple[int, int, int, int] | None, overlay_hwnd: int | None = None):
        """Poll hotbar keys (1-9, Scroll Wheel, Q/Tab), E for Inventory, F10 for Options, Space for Jump, F for Offhand, and Free Mouse-Look."""
        def pressed(vk: int) -> bool:
            state = user32.GetAsyncKeyState(vk)
            down = (state & 0x8000) != 0
            tapped = (state & 0x0001) != 0
            was = self.key_state.get(vk, False)
            self.key_state[vk] = down
            return (down and not was) or (tapped and not was)

        def edge(vk: int) -> tuple[bool, bool]:
            down = (user32.GetAsyncKeyState(vk) & 0x8000) != 0
            was = self.key_state.get(vk, False)
            self.key_state[vk] = down
            return down != was, down

        # F5: toggle Free Mouse-Look lock (enabled automatically when a match starts!)
        if pressed(0x74):  # VK_F5
            self.mouse_look_locked = not self.mouse_look_locked
            self._last_cursor = None
            print(f"[Passthrough] Free Mouse-Look Lock: {self.mouse_look_locked} (press F5 to toggle, or hold Alt for cursor)")

        # F6: manually toggle Match Mode
        if pressed(0x75):  # VK_F6
            new_state = not self.in_match
            self.manual_override = new_state if new_state else None
            self.set_in_match(new_state, "F6 hotkey")

        # F7: toggle passthrough overlay
        if pressed(0x76):  # VK_F7
            self.enabled = not self.enabled
            print(f"[Passthrough] Overlay enabled: {self.enabled}")

        # F8: re-level ground & restock starter items
        if pressed(0x77):  # VK_F8
            self.send_arena_ground(restock_items=True)

        # F12: Clean exit / kill switch requested by user!
        if pressed(0x7B):  # VK_F12
            print("\n[Passthrough] Exit requested by user (F12 keybind) -> stopping cleanly...")
            self.running = False
            self.input_catcher.update_state(False, None)
            return

        if not self.in_match:
            self.input_catcher.update_state(False, None)
            return

        # E key (or user's custom inventory key in options.txt) -> open Minecraft Inventory screen!
        # Only open from here when not already open. When already open, closing is handled by Escape,
        # clicking outside, or typing without edit-box focus in Minecraft. This ensures typing 'e'
        # in the creative search bar never closes the screen!
        if not self.inventory_open and pressed(self.vk_inventory):
            self.inventory_open = True
            self._last_cursor = None
            self._last_gui_mouse = None
            self.send_mc({"t": "inv", "open": True})
            if target_rect is not None:
                rx, ry, rw, rh = target_rect
                user32.SetCursorPos(rx + rw // 2, ry + rh // 2)
            print("[Passthrough] Opened Minecraft Inventory")

        # F10 key -> open / close Minecraft Options screen directly in the passthrough!
        if pressed(0x79):  # VK_F10
            self.inventory_open = not self.inventory_open
            self._last_cursor = None
            self._last_gui_mouse = None
            self.send_mc({"t": "key", "k": "options", "down": True})
            if not self.inventory_open:
                self.input_catcher.update_state(False, None)
                if mumu_hwnd:
                    try:
                        user32.SetForegroundWindow(mumu_hwnd)
                    except Exception:
                        pass
            elif target_rect is not None:
                rx, ry, rw, rh = target_rect
                user32.SetCursorPos(rx + rw // 2, ry + rh // 2)
            print(f"[Passthrough] Toggled Minecraft Options Menu (F10): {'OPEN' if self.inventory_open else 'CLOSED'}")

        # Escape key -> close Minecraft Inventory/Options/Chat if open
        if self.inventory_open and pressed(0x1B):  # VK_ESCAPE
            self.inventory_open = False
            self._last_cursor = None
            self._last_gui_mouse = None
            self.input_catcher.update_state(False, None)
            self.send_mc({"t": "inv", "open": False})
            self.send_mc({"t": "type", "key": 256})
            if mumu_hwnd:
                try:
                    user32.SetForegroundWindow(mumu_hwnd)
                except Exception:
                    pass

        # T key (or user's custom chat key in options.txt) -> open Minecraft Chat
        if not self.inventory_open and pressed(self.vk_chat):
            self.inventory_open = True
            self._last_cursor = None
            self._last_gui_mouse = None
            self.input_catcher.update_state(True, target_rect)
            self.send_mc({"t": "chat", "cmd": False})
            print(f"[Passthrough] Opened Minecraft Chat ({self._vk_label(self.vk_chat)})")

        # / key (or user's custom command key in options.txt) -> open Minecraft Command prompt
        if not self.inventory_open and pressed(self.vk_command):
            self.inventory_open = True
            self._last_cursor = None
            self._last_gui_mouse = None
            self.input_catcher.update_state(True, target_rect)
            self.send_mc({"t": "chat", "cmd": True})
            print(f"[Passthrough] Opened Minecraft Command prompt ({self._vk_label(self.vk_command)})")

        # 1) Number keys 1..9 (or custom hotbar keys in options.txt) -> select hotbar slot (or assign hovered Inventory item to slot 1..9!)
        for i in range(9):
            custom_vk = self.vk_hotbar[i]
            if pressed(0x31 + i) or pressed(0x61 + i) or (custom_vk not in (0x31 + i, 0x61 + i) and pressed(custom_vk)):
                self.select_hotbar_slot(i)

        # 2) Gameplay keys when Inventory is closed: Space (Jump), F (Swap Offhand), Q/G (Drop Item), Tab (Cycle Hotbar)
        if not self.inventory_open:
            if pressed(0x20) or (self.vk_jump != 0x20 and pressed(self.vk_jump)):  # Jump!
                if self.jump_offset_y <= 0.05:
                    self.jump_vel_y = 0.42
            if pressed(self.vk_swap_offhand):  # F -> Swap Offhand
                self.send_mc({"t": "key", "k": "swap", "down": True})
                self.send_mc({"t": "key", "k": "swap", "down": False})
            if pressed(self.vk_drop):  # Drop Item (respects Q or user's drop binding!)
                self.send_mc({"t": "key", "k": "drop", "down": True})
                self.send_mc({"t": "key", "k": "drop", "down": False})
            if self.vk_drop != ord("Q") and pressed(ord("Q")):
                self.scroll_hotbar(1)
            if pressed(0x09):  # Tab
                self.scroll_hotbar(-1)

        # Check if MuMu Player is the active foreground window (and Left Alt is not held to temporarily free cursor)
        alt_held = (user32.GetAsyncKeyState(0x12) & 0x8000) != 0  # VK_MENU (Alt)
        mumu_focused = False
        if mumu_hwnd is not None:
            fg_hwnd = user32.GetForegroundWindow()
            if fg_hwnd == mumu_hwnd or (self.inventory_open and fg_hwnd in (self.input_catcher.hwnd, overlay_hwnd)):
                mumu_focused = True
            elif fg_hwnd:
                fg_pid = wintypes.DWORD(0)
                mumu_pid = wintypes.DWORD(0)
                user32.GetWindowThreadProcessId(fg_hwnd, ctypes.byref(fg_pid))
                user32.GetWindowThreadProcessId(mumu_hwnd, ctypes.byref(mumu_pid))
                if fg_pid.value != 0 and fg_pid.value == mumu_pid.value:
                    mumu_focused = True

        active_free_look = (
            self.mouse_look_locked
            and not self.inventory_open
            and mumu_focused
            and not alt_held
            and target_rect is not None
        )
        # IMPORTANT: Keep MouseInputCatcher 100% HIDDEN during gameplay so MuMu Player's WASD keymapper works!
        # Only show MouseInputCatcher while the Minecraft Inventory ('E') or Options ('F10') is open!
        catcher_active = self.inventory_open and mumu_focused and not alt_held and target_rect is not None
        self.input_catcher.update_state(catcher_active, target_rect)

        pt_cur = POINT()
        user32.GetCursorPos(ctypes.byref(pt_cur))

        in_mumu = False
        if target_rect is not None:
            rx, ry, rw, rh = target_rect
            in_mumu = (rx <= pt_cur.x <= rx + rw) and (ry + 36 <= pt_cur.y <= ry + rh)

        # If user clicks inside MuMu while MuMu was not focused, immediately restore foreground focus to MuMu!
        if in_mumu and not self.inventory_open and not mumu_focused:
            if (user32.GetAsyncKeyState(0x01) & 0x8000) or (user32.GetAsyncKeyState(0x02) & 0x8000):
                if mumu_hwnd:
                    try:
                        user32.SetForegroundWindow(mumu_hwnd)
                        mumu_focused = True
                    except Exception:
                        pass

        # Handle Mouse Scroll Wheel (captured via Raw Input RIDEV_INPUTSINK even when MouseInputCatcher is hidden!)
        wheel_steps = self.input_catcher.pop_wheel_steps()
        if wheel_steps != 0 and (mumu_focused or in_mumu):
            if self.inventory_open and target_rect is not None:
                rx, ry, rw, rh = target_rect
                vw = max(1, min(1920, rw))
                vh = max(1, min(1080, rh))
                dst_x = rx + (rw - vw) // 2
                dst_y = ry + (rh - vh)
                nx = round(max(0.0, min(1.0, (pt_cur.x - dst_x) / float(vw))), 5)
                ny = round(max(0.0, min(1.0, (pt_cur.y - dst_y) / float(vh))), 5)
                self.send_mc({"t": "mscroll", "d": float(wheel_steps), "x": nx, "y": ny})
            else:
                self.scroll_hotbar(1 if wheel_steps > 0 else -1)

        # If Minecraft Inventory ('E'), Chat ('T'/'/'), or Options ('F10') is open: forward clicks AND typing!
        if self.inventory_open and target_rect is not None:
            changed_l, down_l = edge(0x01)
            changed_r, down_r = edge(0x02)
            rx, ry, rw, rh = target_rect
            vw = max(1, min(1920, rw))
            vh = max(1, min(1080, rh))
            dst_x = rx + (rw - vw) // 2
            dst_y = ry + (rh - vh)
            nx = round(max(0.0, min(1.0, (pt_cur.x - dst_x) / float(vw))), 5)
            ny = round(max(0.0, min(1.0, (pt_cur.y - dst_y) / float(vh))), 5)
            shift_mod = 1 if (user32.GetAsyncKeyState(0x10) & 0x8000) != 0 else 0
            if self._last_gui_mouse != (nx, ny):
                self._last_gui_mouse = (nx, ny)
                self.send_mc({"t": "mouse", "x": nx, "y": ny})
            if changed_l:
                self.send_mc({"t": "mbtn", "b": 0, "mod": shift_mod, "down": down_l, "x": nx, "y": ny})
            if changed_r:
                self.send_mc({"t": "mbtn", "b": 1, "mod": shift_mod, "down": down_r, "x": nx, "y": ny})

            # Forward keyboard typing into ChatScreen / Creative Search / GUI
            shift_held = (user32.GetAsyncKeyState(0x10) & 0x8000) != 0
            ctrl_held = (user32.GetAsyncKeyState(0x11) & 0x8000) != 0
            mod_flags = (1 if shift_held else 0) | (2 if ctrl_held else 0)

            # Enter -> execute command / send chat!
            if pressed(0x0D):
                self.send_mc({"t": "type", "key": 257, "mod": mod_flags})
            # Tab -> auto-complete command / focus next widget!
            if pressed(0x09):
                self.send_mc({"t": "type", "key": 258, "mod": mod_flags})
            # Escape
            if pressed(0x1B):
                self.send_mc({"t": "type", "key": 256, "mod": mod_flags})
            # Left Arrow
            if pressed(0x25):
                self.send_mc({"t": "type", "key": 263, "mod": mod_flags})
            # Right Arrow
            if pressed(0x27):
                self.send_mc({"t": "type", "key": 262, "mod": mod_flags})
            # Up Arrow
            if pressed(0x26):
                self.send_mc({"t": "type", "key": 265, "mod": mod_flags})
            # Down Arrow
            if pressed(0x28):
                self.send_mc({"t": "type", "key": 264, "mod": mod_flags})
            # Home
            if pressed(0x24):
                self.send_mc({"t": "type", "key": 268, "mod": mod_flags})
            # End
            if pressed(0x23):
                self.send_mc({"t": "type", "key": 269, "mod": mod_flags})
            # Space
            if pressed(0x20):
                self.send_mc({"t": "type", "char": " "})

            # Backspace & Delete with key-repeat support!
            now = time.perf_counter()
            # 0x08: VK_BACK -> GLFW_KEY_BACKSPACE (259)
            if user32.GetAsyncKeyState(0x08) & 0x8000:
                if 0x08 not in self._key_hold_start:
                    self._key_hold_start[0x08] = now
                    self._key_last_repeat[0x08] = now
                    self.send_mc({"t": "type", "key": 259, "mod": mod_flags})
                elif (now - self._key_hold_start[0x08]) > 0.35 and (now - self._key_last_repeat[0x08]) >= 0.05:
                    self._key_last_repeat[0x08] = now
                    self.send_mc({"t": "type", "key": 259, "mod": mod_flags})
            else:
                self._key_hold_start.pop(0x08, None)
                self._key_last_repeat.pop(0x08, None)

            # 0x2E: VK_DELETE -> GLFW_KEY_DELETE (261)
            if user32.GetAsyncKeyState(0x2E) & 0x8000:
                if 0x2E not in self._key_hold_start:
                    self._key_hold_start[0x2E] = now
                    self._key_last_repeat[0x2E] = now
                    self.send_mc({"t": "type", "key": 261, "mod": mod_flags})
                elif (now - self._key_hold_start[0x2E]) > 0.35 and (now - self._key_last_repeat[0x2E]) >= 0.05:
                    self._key_last_repeat[0x2E] = now
                    self.send_mc({"t": "type", "key": 261, "mod": mod_flags})
            else:
                self._key_hold_start.pop(0x2E, None)
                self._key_last_repeat.pop(0x2E, None)

            # Ctrl+A (Select All), Ctrl+C (Copy), Ctrl+V (Paste), Ctrl+X (Cut)
            if ctrl_held:
                if pressed(ord("A")):
                    self.send_mc({"t": "type", "key": 65, "mod": 2})
                elif pressed(ord("C")):
                    self.send_mc({"t": "type", "key": 67, "mod": 2})
                elif pressed(ord("V")):
                    self.send_mc({"t": "type", "key": 86, "mod": 2})
                elif pressed(ord("X")):
                    self.send_mc({"t": "type", "key": 88, "mod": 2})
            else:
                # Letters A-Z
                for vk in range(0x41, 0x5B):
                    if pressed(vk):
                        ch = chr(vk) if shift_held else chr(vk).lower()
                        self.send_mc({"t": "type", "char": ch})

            # Digits 0-9
            num_shift = ")!@#$%^&*("
            for vk in range(0x30, 0x3A):
                if pressed(vk):
                    ch = num_shift[vk - 0x30] if shift_held else chr(vk)
                    self.send_mc({"t": "type", "char": ch})

            # Symbols & Punctuation
            sym_map = {
                0xBF: ("?", "/"),
                0xBE: (">", "."),
                0xBC: ("<", ","),
                0xBA: (":", ";"),
                0xDE: ('"', "'"),
                0xDB: ("{", "["),
                0xDD: ("}", "]"),
                0xDC: ("|", "\\"),
                0xBD: ("_", "-"),
                0xBB: ("+", "="),
                0xC0: ("~", "`"),
            }
            for vk, (s_shifted, s_normal) in sym_map.items():
                if pressed(vk):
                    ch = s_shifted if shift_held else s_normal
                    self.send_mc({"t": "type", "char": ch})

            return

        # Gameplay Attack & Use controls: respects the user's personal Minecraft Attack and Use key/mouse bindings!
        changed_atk, down_atk = edge(self.vk_attack)
        changed_use, down_use = edge(self.vk_use) if self.vk_use != self.vk_attack else (False, False)

        # Pick Block (Middle Click by default, or user's custom pickItem binding)
        if self.vk_pick not in (self.vk_attack, self.vk_use) and pressed(self.vk_pick) and (active_free_look or in_mumu):
            self.send_mc({"t": "key", "k": "pick", "down": True})
            self.send_mc({"t": "key", "k": "pick", "down": False})

        # Check if cursor is directly over one of the 9 hotbar slots at the bottom-center of MuMu (when cursor is unlocked)
        clicked_hotbar_slot: int | None = None
        if target_rect is not None and not active_free_look:
            rx, ry, rw, rh = target_rect
            hb_w = min(736, rw)
            hb_x0 = rx + (rw - hb_w) // 2
            if (hb_x0 <= pt_cur.x < hb_x0 + hb_w) and (ry + rh - 104 <= pt_cur.y <= ry + rh):
                clicked_hotbar_slot = max(0, min(8, int((pt_cur.x - hb_x0) * 9 // hb_w)))

        if active_free_look:
            # In Free Mouse-Look mode, the user's Attack button/key directly triggers Minecraft attack / block mining / crystal detonation!
            if changed_atk:
                self.send_mc({"t": "key", "k": "attack", "down": down_atk})
                if down_atk:
                    self._perform_melee_sweep()
        else:
            # When cursor is unlocked (Alt held or F5 toggled off), allow clicking hotbar slots or attacking inside MuMu
            if changed_atk and down_atk:
                if clicked_hotbar_slot is not None:
                    self.select_hotbar_slot(clicked_hotbar_slot)
                    self._atk_down_pos = None
                elif in_mumu:
                    self._atk_down_pos = (pt_cur.x, pt_cur.y)
                    self._atk_down_time = time.perf_counter()
                    self._atk_is_drag = False
                    self._atk_sent = False
                else:
                    self._atk_down_pos = None
            elif down_atk and self._atk_down_pos is not None:
                if math.hypot(pt_cur.x - self._atk_down_pos[0], pt_cur.y - self._atk_down_pos[1]) > 6.0:
                    self._atk_is_drag = True
                    if self._atk_sent:
                        self.send_mc({"t": "key", "k": "attack", "down": False})
                        self._atk_sent = False
                elif not self._atk_sent and (time.perf_counter() - self._atk_down_time) >= 0.14:
                    self.send_mc({"t": "key", "k": "attack", "down": True})
                    self._atk_sent = True
                    self._perform_melee_sweep()
            elif changed_atk and not down_atk:
                if self._atk_down_pos is not None:
                    if not self._atk_is_drag and not self._atk_sent:
                        self.send_mc({"t": "key", "k": "attack", "down": True})
                        self.send_mc({"t": "key", "k": "attack", "down": False})
                        self._perform_melee_sweep()
                    elif self._atk_sent:
                        self.send_mc({"t": "key", "k": "attack", "down": False})
                self._atk_down_pos = None
                self._atk_is_drag = False
                self._atk_sent = False

        # User's Use button/key -> Minecraft use (place block / End Crystal / Respawn Anchor / Ender Pearl / Wind Charge / TNT / Bow)
        if changed_use:
            if down_use and (active_free_look or in_mumu):
                self.send_mc({"t": "key", "k": "use", "down": True})
                self._use_down_sent = True
            elif not down_use and self._use_down_sent:
                self.send_mc({"t": "key", "k": "use", "down": False})
                self._use_down_sent = False

        # Arrow keys (Left/Right/Up/Down) -> rotate First-Person camera in Brawl Stars & Minecraft
        dyaw = 0.0
        dpitch = 0.0
        if user32.GetAsyncKeyState(0x25) & 0x8000:  # VK_LEFT
            dyaw += 0.045
        if user32.GetAsyncKeyState(0x27) & 0x8000:  # VK_RIGHT
            dyaw -= 0.045
        if user32.GetAsyncKeyState(0x26) & 0x8000:  # VK_UP
            dpitch -= 0.035
        if user32.GetAsyncKeyState(0x28) & 0x8000:  # VK_DOWN
            dpitch += 0.035

        # Free Mouse-Look (scaled by the user's Minecraft mouseSensitivity setting!)
        sens_scale = max(0.15, min(3.0, self.mc_sensitivity / 0.5))
        look_speed = 0.0032 * sens_scale
        mmb_down = (user32.GetAsyncKeyState(0x04) & 0x8000) != 0
        if active_free_look or mmb_down:
            pt = POINT()
            user32.GetCursorPos(ctypes.byref(pt))
            if active_free_look and target_rect is not None:
                # Lock cursor in the EXACT DEAD CENTER of MuMu's game window!
                cx = target_rect[0] + target_rect[2] // 2
                cy = target_rect[1] + target_rect[3] // 2
                if self._last_cursor is None:
                    user32.SetCursorPos(cx, cy)
                    self._last_cursor = (cx, cy)
                else:
                    mdx = pt.x - cx
                    mdy = pt.y - cy
                    if mdx != 0 or mdy != 0:
                        dyaw -= mdx * look_speed
                        dpitch += mdy * look_speed
                        user32.SetCursorPos(cx, cy)
                    self._last_cursor = (cx, cy)
            else:
                if self._last_cursor is not None:
                    mdx = pt.x - self._last_cursor[0]
                    mdy = pt.y - self._last_cursor[1]
                    dyaw -= mdx * look_speed
                    dpitch += mdy * look_speed
                self._last_cursor = (pt.x, pt.y)
        else:
            self._last_cursor = None

        if dyaw != 0.0 or dpitch != 0.0:
            self.mem_bridge.add_look_delta(dyaw, dpitch)

    def step_camera(self):
        """Send First-Person camera (fp=true) and enemy brawler proxy mobs ('peds') to Minecraft."""
        if not self.connect_mc():
            return
        if not self.in_match:
            return
        if not self.ground_sent:
            self.send_arena_ground(restock_items=not self.items_stocked)

        self.frame_num += 1
        if self.frame_num in (45, 120):
            self.send_arena_ground(restock_items=False)
        elif self.frame_num % 60 == 0:
            self._reinforce_local_ground()

        feet_y = self.cam_y - 1.62
        cam_msg = {
            "t": "cam",
            "f": self.frame_num,
            "p": [round(self.cam_x, 4), round(self.cam_y, 4), round(self.cam_z, 4)],
            "r": [round(self.cam_yaw, 3), round(self.cam_pitch, 3), 0.0],
            "fov": round(self.fov, 2),
            "fp": True,
            "pl": [round(self.cam_x, 4), round(feet_y, 4), round(self.cam_z, 4)],
            "h": round(self.cam_yaw, 2),
        }
        self.send_mc(cam_msg)

        if self.frame_num % 3 == 0:
            peds_list = [[h_id, round(mx, 3), 64.0, round(mz, 3)] for h_id, mx, mz, bhp, _, _, _ in self.brawlers]
            self.send_mc({"t": "peds", "p": peds_list})


def main():
    print("==================================================================")
    print("  BSL v68 (MuMu Player) x Minecraft 26.3 — First-Person Passthrough")
    print("==================================================================")
    setup_modrinth_jar()
    apply_mumu_patch(remove=False, launch_app=False)
    ensure_brawl_server_running()
    mem_bridge = MumuMemoryBridge()

    print("------------------------------------------------------------------")
    print("[Ready] Setup complete! Now:")
    print("  1. Launch Minecraft (Fabric 26.3) Yourself in Modrinth App")
    print("     (Its window & taskbar icon will automatically hide at 60+ FPS!)")
    print("  2. Open BSL v68 Yourself in MuMu Player and start a match")
    print("  3. Closing this script (Ctrl+C or X) stops Minecraft Java & Server")
    print("------------------------------------------------------------------")
    print("Controls (active during a Brawl Stars match):")
    print("  WASD / Space            : Move & Jump in First-Person Arena (camera-relative)")
    print("  Move Mouse              : Free Mouse-Look (uses your Minecraft Mouse Sensitivity!)")
    print("  Mouse Scroll Wheel      : Switch Minecraft Hotbar Item (intercepted so it never moves you in MuMu!)")
    print("  1-9 / Q / Tab / F / G   : Hotbar 1-9, Cycle Hotbar, Swap Offhand (F), Drop Item (G)")
    print("  E                       : Open / Close Minecraft Inventory (with full mouse drag & drop)")
    print("  F10                     : Open / Close Minecraft Options Menu (change GUI Scale, Sensitivity, etc.)")
    print("  Left Click              : Minecraft Attack (Swing Sword / Mace Smash / Trident / Break Block)")
    print("  Right Click             : Minecraft Use (Wind Charge Jump / Place Block / Light TNT / Fireball / Bow)")
    print("  F5                      : Toggle Free Mouse-Look Lock")
    print("  F6                      : Manually Toggle Match Mode (Black Screen <-> Active Match)")
    print("  F7                      : Toggle MuMu Window Overlay")
    print("  F8                      : Re-level Arena Ground & Restock Hotbar/Inventory")
    print("------------------------------------------------------------------")

    input_catcher = MouseInputCatcher()
    host = BslMinecraftPassthrough(mem_bridge, input_catcher)
    reader = MCFrameReader()
    overlay = PassthroughOverlay(1280, 720)

    try:
        while host.running:
            t0 = time.perf_counter()
            overlay.pump_messages()
            hide_and_unthrottle_minecraft_window()
            host.sync_from_memory_bridge()
            host.poll_bsl_events()
            host.poll_mc_events()

            mumu = find_mumu_window()
            mumu_hwnd = mumu[0] if mumu else None
            target_rect = mumu[1] if mumu else None
            host.poll_inputs(mumu_hwnd, target_rect, overlay.hwnd)
            if not host.running:
                break
            host.step_camera()

            if target_rect and host.mc_sock is not None and (target_rect[2], target_rect[3]) != host.last_view_size:
                host.last_view_size = (target_rect[2], target_rect[3])
                host.send_mc({"t": "view", "w": min(1920, target_rect[2]), "h": min(1080, target_rect[3])})

            if host.enabled and host.in_match and target_rect is not None:
                frame = reader.latest_bgra()
                if frame is not None:
                    _, _, bgra = frame
                    overlay.update_frame(bgra, target_rect)
            else:
                overlay.set_visible(False)

            elapsed = time.perf_counter() - t0
            if elapsed < 1.0 / 60.0:
                time.sleep((1.0 / 60.0) - elapsed)
    except KeyboardInterrupt:
        input_catcher.update_state(False, None)
        overlay.set_visible(False)
        cleanup_all_processes()
        print("\n[Passthrough] Stopped.")
    finally:
        input_catcher.update_state(False, None)
        overlay.set_visible(False)
        cleanup_all_processes()


if __name__ == "__main__":
    main()

