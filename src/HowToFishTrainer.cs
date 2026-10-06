using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrainerForm());
    }
}

internal sealed class TrainerForm : Form
{
    private const int HotkeyId = 7;
    private const int SteamAppId = 4001890;
    private readonly string _root = ResolveGameRoot();
    private readonly Label _status = new();
    private readonly Button _launch = new();
    private Process? _game;
    private bool _patchActive;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    private const int WM_HOTKEY = 0x0312;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    private static string ResolveGameRoot()
    {
        string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string configPath = Path.Combine(baseDir, "game-path.txt");
        if (File.Exists(configPath))
        {
            string configuredRoot = File.ReadAllText(configPath).Trim();
            if (Directory.Exists(Path.Combine(configuredRoot, "How to Fish_Data"))) return Path.GetFullPath(configuredRoot);
        }
        if (Directory.Exists(Path.Combine(baseDir, "How to Fish_Data"))) return baseDir;
        string? parent = Directory.GetParent(baseDir)?.FullName;
        return parent != null && Directory.Exists(Path.Combine(parent, "How to Fish_Data")) ? parent : baseDir;
    }
    public TrainerForm()
    {
        Text = "How to Fish · Single-Player Control Panel";
        ClientSize = new Size(570, 765);
        MinimumSize = new Size(570, 765);
        MaximumSize = new Size(570, 765);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(21, 29, 38);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);
        BuildUi();
        Shown += (_, _) =>
        {
            if (!RegisterHotKey(Handle, HotkeyId, 0, (uint)Keys.F8)) _status.Text = "F8 hotkey unavailable. Use the panel buttons instead.";
            RefreshGame();
            Log("Panel opened. DLL backup exists: " + File.Exists(BackupPath) + "; sizeof(INPUT)=" + Marshal.SizeOf<INPUT>());
        };
        FormClosing += (_, e) =>
        {
            if (_game != null && !_game.HasExited)
            {
                e.Cancel = true;
                Hide();
                _status.Text = "Panel hidden. The original DLL will be restored when the game closes. Press F8 to show the panel.";
                return;
            }
            UnregisterHotKey(Handle, HotkeyId);
        };
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            Visible = !Visible;
            if (Visible) { Show(); Activate(); }
        }
        base.WndProc(ref m);
    }

    private void BuildUi()
    {
        var title = new Label { Text = "HOW TO FISH  |  SOLO TOOL", Font = new Font("Segoe UI Semibold", 16F), ForeColor = Color.FromArgb(120, 205, 255), AutoSize = true, Location = new Point(22, 18) };
        Controls.Add(title);
        _status.SetBounds(24, 57, 518, 46);
        _status.ForeColor = Color.FromArgb(225, 230, 235);
        _status.Text = "Game not started. Use the button below to launch it.";
        Controls.Add(_status);

        _launch.Text = "Enable Cheats and Launch Game";
        StyleButton(_launch, Color.FromArgb(37, 112, 155));
        _launch.SetBounds(24, 106, 518, 40);
        _launch.Click += async (_, _) => await LaunchGame();
        Controls.Add(_launch);

        AddSection("HEALTH & COMBAT", 165);
        AddToggle("Invincibility", "/godmode", 24, 194);
        AddToggle("One Hit", "/oneshot", 287, 194);
        AddAction("Defeat Boss", "/killboss", 24, 238);
        AddAction("Kill All Creatures", "/killallcreatures", 287, 238);
        AddAction("Reset Creatures", "/resetallcreatures", 24, 282);

        AddSection("PROGRESSION & RESOURCES", 338);
        AddAction("+9,999 Cash", "/addmoney", 24, 367);
        AddMoneyAction("+$99,999", 0x4D, false, 287, 367);
        AddMoneyAction("−$99,999", 0x4E, false, 24, 409);
        AddMoneyAction("+999 Euros", 0x4D, true, 287, 409);
        AddMoneyAction("−999 Euros", 0x4E, true, 24, 451);
        AddAction("Next Island", "/nextisland", 287, 451);
        AddAction("Previous Island", "/previsland", 24, 493);
        AddAction("Unlock Boat", "/boat", 287, 493);
        AddAction("Open Grill", "/grill", 24, 535);
        AddAction("Finish Game", "/finishgame", 287, 535);

        AddSection("ITEMS & EXTRAS", 579);
        var spawnName = new TextBox { Text = "", PlaceholderText = "Item/fish spawnable name", BackColor = Color.FromArgb(35, 45, 56), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        spawnName.SetBounds(24, 607, 333, 34);
        Controls.Add(spawnName);
        var spawn = new Button { Text = "Spawn Item" };
        StyleButton(spawn, Color.FromArgb(55, 93, 122));
        spawn.SetBounds(367, 607, 175, 34);
        spawn.Click += async (_, _) => { var name = spawnName.Text.Trim(); if (name.Length == 0) { _status.Text = "Enter an item or fish name first."; return; } await SendCommand("/spawn " + name); };
        Controls.Add(spawn);
        var extra = new TextBox { PlaceholderText = "Extra command: /allskins, /unlockachievements, etc.", BackColor = Color.FromArgb(35, 45, 56), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        extra.SetBounds(24, 648, 333, 32);
        Controls.Add(extra);
        var sendExtra = new Button { Text = "Send Command" };
        StyleButton(sendExtra, Color.FromArgb(55, 93, 122));
        sendExtra.SetBounds(367, 648, 175, 32);
        sendExtra.Click += async (_, _) => { var cmd = extra.Text.Trim(); if (cmd.Length == 0) { _status.Text = "Enter a command first."; return; } await SendCommand(cmd.StartsWith("/") ? cmd : "/" + cmd); };
        Controls.Add(sendExtra);
        var hint = new Label { Text = "Offline solo only. Online lobbies, joins and invites are blocked. F8: show/hide.", ForeColor = Color.FromArgb(175, 190, 202), AutoSize = false };
        hint.SetBounds(24, 684, 518, 32);
        Controls.Add(hint);

        var restore = new Button { Text = "Restore Original Game File" };
        StyleButton(restore, Color.FromArgb(105, 70, 63));
        restore.SetBounds(24, 720, 518, 30);
        restore.Click += (_, _) => RestoreOriginal(false);
        Controls.Add(restore);
    }

    private void AddSection(string text, int y) => Controls.Add(new Label { Text = text, Font = new Font("Segoe UI Semibold", 10F), ForeColor = Color.FromArgb(122, 162, 190), AutoSize = true, Location = new Point(24, y) });

    private void AddToggle(string text, string command, int x, int y)
    {
        var button = new Button { Text = "◯  " + text + "  |  ON/OFF" };
        StyleButton(button, Color.FromArgb(61, 81, 101));
        button.SetBounds(x, y, 255, 38);
        button.Click += async (_, _) => { await SendCommand(command); button.Text = (button.Text.StartsWith("◯") ? "●" : "◯") + button.Text[1..]; };
        Controls.Add(button);
    }

    private void AddAction(string text, string command, int x, int y)
    {
        var button = new Button { Text = text };
        StyleButton(button, Color.FromArgb(55, 93, 122));
        button.SetBounds(x, y, 255, 38);
        button.Click += async (_, _) => await SendCommand(command);
        Controls.Add(button);
    }

    private void AddMoneyAction(string text, ushort key, bool euro, int x, int y)
    {
        var button = new Button { Text = text };
        StyleButton(button, Color.FromArgb(55, 93, 122));
        button.SetBounds(x, y, 255, 38);
        button.Click += async (_, _) => await SendMoneyShortcut(key, euro);
        Controls.Add(button);
    }

    private static void StyleButton(Button button, Color color)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(85, 111, 132);
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.Cursor = Cursors.Hand;
    }

    private string GamePath => Path.Combine(_root, "How to Fish.exe");
    private string AssemblyPath => Path.Combine(_root, "How to Fish_Data", "Managed", "Assembly-CSharp.dll");
    private string BackupPath => AssemblyPath + ".solo-backup";

    private async Task LaunchGame()
    {
        RefreshGame();
        if (_game is { HasExited: false })
        {
            _status.Text = File.Exists(BackupPath)
                ? "The game is already running with a backup present. Use a session launched from this panel."
                : "The game is already running without the patch. Save, exit the game, then launch it with this button.";
            Log("Launch clicked while game was already running. PID=" + _game.Id + ", backup=" + File.Exists(BackupPath));
            MessageBox.Show(this, _status.Text, "How to Fish", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            if (!File.Exists(GamePath) || !File.Exists(AssemblyPath)) throw new FileNotFoundException("The game executable or Assembly-CSharp.dll could not be found.");
            var steamExe = GetSteamExePath();
            if (!File.Exists(steamExe)) throw new FileNotFoundException("Steam.exe could not be found. Check the Steam installation path.", steamExe);
            EnableCheatsPatch();
            _patchActive = true;
            Log("DLL patch applied and backup created.");
            var steamStart = Process.Start(new ProcessStartInfo(steamExe) { Arguments = "-applaunch " + SteamAppId, WorkingDirectory = Path.GetDirectoryName(steamExe)!, UseShellExecute = true });
            if (steamStart == null) throw new InvalidOperationException("Steam did not accept the launch request.");
            _status.Text = "Launching the game through Steam; waiting for the game process...";
            Log("Steam -applaunch request sent. AppID=" + SteamAppId);
            _game = await WaitForStableGameProcess();
            if (_game == null)
            {
                _status.Text = "Steam did not start the game. The backup is preserved; check the log file.";
                Log("No stable game process found within 60 seconds after Steam launch. Patch was not restored.");
                return;
            }
            _game.EnableRaisingEvents = true;
            _game.Exited += (_, _) => { Log("Launched game process exited; restoring the original DLL."); BeginInvoke(new Action(() => RestoreOriginal(true))); };
            _status.Text = "Game launched. Online multiplayer is blocked. Enter an offline solo world to use the panel.";
            Log("Stable game process attached. PID=" + _game.Id);
        }
        catch (Exception ex) { Log("Launch/patch error: " + ex); _status.Text = "Launch failed: " + ex.Message; MessageBox.Show(this, ex.Message, "Launch Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void EnableCheatsPatch()
    {
        if (File.Exists(BackupPath)) File.Copy(BackupPath, AssemblyPath, true);
        else File.Copy(AssemblyPath, BackupPath, false);
        var bytes = File.ReadAllBytes(AssemblyPath);
        using var stream = new MemoryStream(bytes, false);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        MethodDefinition? target = null;
        foreach (var typeHandle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(typeHandle);
            if (md.GetString(type.Name) != "ClientSettings") continue;
            foreach (var methodHandle in type.GetMethods())
            {
                var method = md.GetMethodDefinition(methodHandle);
                if (md.GetString(method.Name) == "get_CheatsEnabled") { target = method; break; }
            }
            break;
        }
        if (target == null) throw new InvalidDataException("CheatsEnabled was not found; the game version may have changed.");
        var body = pe.GetMethodBody(target.Value.RelativeVirtualAddress);
        var il = body.GetILBytes() ?? throw new InvalidDataException("Could not read method IL.");
        if (il.Length != 6 || il[0] != 0x7e || il[5] != 0x2a) throw new InvalidDataException("Unexpected cheat-check method format; no game file was patched.");
        int offset = RvaToOffset(pe.PEHeaders, target.Value.RelativeVirtualAddress);
        if (bytes[offset] >> 2 != 6 || (bytes[offset] & 3) != 2) throw new InvalidDataException("Unexpected method-body format; no game file was patched.");
        bytes[offset + 1] = 0x17; // ldc.i4.1
        bytes[offset + 2] = 0x2a; // ret
        bytes[offset + 3] = bytes[offset + 4] = bytes[offset + 5] = bytes[offset + 6] = 0x00; // nop padding
        PatchOfflineMultiplayerEntryPoints(md, pe, bytes);
        File.WriteAllBytes(AssemblyPath, bytes);
    }

    private static void PatchOfflineMultiplayerEntryPoints(MetadataReader md, PEReader pe, byte[] bytes)
    {
        // The game's offline solo path connects only to localhost. Block the distinct
        // Steam/online lobby entry points while leaving offline local hosting intact.
        (string Type, string Method, bool ReturnsBoolean)[] targets =
        {
            ("ButtonManager", "CreateOnlineLobbyButton", false),
            ("ButtonManager", "DeveloperJoinLobbyButton", false),
            ("ConnectionManager", "CreateOnlineLobby", true),
            ("ConnectionManager", "JoinOnlineLobby", true),
            ("SteamManager", "CreateLobby", false),
            ("SteamManager", "JoinLobby", false),
            ("SteamManager", "JoinFriend", false),
            ("SteamManager", "OnJoinRequested", false),
        };

        foreach (var target in targets)
        {
            MethodDefinitionHandle match = default;
            foreach (var typeHandle in md.TypeDefinitions)
            {
                var type = md.GetTypeDefinition(typeHandle);
                if (md.GetString(type.Name) != target.Type) continue;
                foreach (var methodHandle in type.GetMethods())
                {
                    var method = md.GetMethodDefinition(methodHandle);
                    if (md.GetString(method.Name) == target.Method) { match = methodHandle; break; }
                }
                break;
            }
            if (match.IsNil) throw new InvalidDataException("Required method for the online multiplayer block was not found: " + target.Type + "." + target.Method);

            var definition = md.GetMethodDefinition(match);
            var signature = md.GetBlobReader(definition.Signature);
            var header = signature.ReadSignatureHeader();
            if (header.IsGeneric) signature.ReadCompressedInteger();
            signature.ReadCompressedInteger(); // parameter count
            byte returnType = signature.ReadByte();
            byte expectedReturnType = target.ReturnsBoolean ? (byte)0x02 : (byte)0x01; // Boolean / Void
            if (returnType != expectedReturnType) throw new InvalidDataException("Method signature changed; the online multiplayer block was not applied: " + target.Type + "." + target.Method);

            var body = pe.GetMethodBody(definition.RelativeVirtualAddress);
            if (body.ExceptionRegions.Length != 0) throw new InvalidDataException("Cannot safely apply the online multiplayer block (exception region found): " + target.Type + "." + target.Method);
            var il = body.GetILBytes() ?? throw new InvalidDataException("Could not read method IL: " + target.Type + "." + target.Method);
            int requiredBytes = target.ReturnsBoolean ? 2 : 1;
            if (il.Length < requiredBytes) throw new InvalidDataException("Method body is shorter than expected: " + target.Type + "." + target.Method);

            int methodOffset = RvaToOffset(pe.PEHeaders, definition.RelativeVirtualAddress);
            byte first = bytes[methodOffset];
            int headerSize = (first & 0x03) == 0x02 ? 1 : (first & 0x03) == 0x03 ? ((bytes[methodOffset + 1] >> 4) * 4) : 0;
            if (headerSize == 0 || headerSize + il.Length > bytes.Length - methodOffset)
                throw new InvalidDataException("Unexpected method-body format: " + target.Type + "." + target.Method);
            int ilOffset = methodOffset + headerSize;
            Array.Fill(bytes, (byte)0x00, ilOffset, il.Length); // unreachable padding
            if (target.ReturnsBoolean) bytes[ilOffset++] = 0x16; // ldc.i4.0
            bytes[ilOffset] = 0x2a; // ret
        }
    }

    private static int RvaToOffset(PEHeaders headers, int rva)
    {
        foreach (var section in headers.SectionHeaders)
        {
            int start = section.VirtualAddress;
            int end = start + Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (rva >= start && rva < end) return section.PointerToRawData + rva - start;
        }
        throw new InvalidDataException("Could not map method RVA to a file offset.");
    }

    private async Task SendCommand(string command)
    {
        RefreshGame();
        if (_game == null || _game.HasExited) { _status.Text = "Launch the game from this panel first."; Log("Command not sent; game process not found: " + command); return; }
        if (!_patchActive) { _status.Text = "The cheat patch is not active for this session. Save, exit the game completely, then relaunch with the button above."; Log("Command not sent; patchActive=false, PID=" + _game.Id + ": " + command); MessageBox.Show(this, _status.Text, "Cheats Not Enabled", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        try
        {
            IntPtr gameWindow = FindGameWindow(_game);
            if (gameWindow == IntPtr.Zero) { _status.Text = "Game window not found. Bring the game to the foreground and try again."; Log("Game window not found; PID=" + _game.Id + ": " + command); return; }
            ShowWindow(gameWindow, 9);
            if (!SetForegroundWindow(gameWindow)) { _status.Text = "Could not focus the game window. Click the game and try again."; Log("SetForegroundWindow failed: " + command); return; }
            await Task.Delay(180);
            SendForegroundKey(0x0D);
            await Task.Delay(180);
            TypeUnicode(command);
            await Task.Delay(180);
            SendForegroundKey(0x0D);
            _status.Text = "Input sent to the game: " + command;
            Log("SendInput delivered input to game: " + command);
        }
        catch (Exception ex) { Log("Command send error: " + ex); _status.Text = "Could not send command: " + ex.Message; }
    }

    private async Task SendMoneyShortcut(ushort key, bool euro)
    {
        RefreshGame();
        if (_game == null || _game.HasExited || !_patchActive) { _status.Text = "The cheat patch is not active. Exit the game and launch it with the button above."; Log("Money shortcut not sent; patch is inactive."); return; }
        IntPtr gameWindow = FindGameWindow(_game);
        if (gameWindow == IntPtr.Zero) { _status.Text = "Game window not found. Bring the game to the foreground and try again."; Log("Game window not found for money shortcut."); return; }
        ShowWindow(gameWindow, 9);
        if (!SetForegroundWindow(gameWindow)) { _status.Text = "Could not focus the game window. Click the game and try again."; return; }
        await Task.Delay(160);
        if (euro) SendKeyDown(0x11); // Ctrl
        SendForegroundKey(key);
        if (euro) SendKeyUp(0x11);
        _status.Text = "Money shortcut sent.";
        Log("Money shortcut sent to game: " + (euro ? "Ctrl+" : "") + (char)key);
    }

    private static IntPtr FindGameWindow(Process process)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out uint ownerPid);
            if (ownerPid != (uint)process.Id) return true;
            found = hWnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private string GetSteamExePath()
    {
        var install = new DirectoryInfo(_root);
        var steamRoot = install.Parent?.Parent?.Parent?.Parent;
        if (steamRoot == null) throw new DirectoryNotFoundException("Could not determine the Steam installation directory.");
        return Path.Combine(steamRoot.FullName, "steam.exe");
    }

    private static async Task<Process?> WaitForStableGameProcess()
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            Process? candidate = null;
            try
            {
                candidate = Process.GetProcessesByName("How to Fish")
                    .Where(p => !p.HasExited)
                    .OrderByDescending(p => p.StartTime)
                    .FirstOrDefault();
            }
            catch { }
            if (candidate != null)
            {
                await Task.Delay(1200);
                try { candidate.Refresh(); if (!candidate.HasExited) return candidate; } catch { }
            }
            await Task.Delay(300);
        }
        return null;
    }

    private void Log(string message)
    {
        try { File.AppendAllText(Path.Combine(_root, "HowToFishTrainer.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine); } catch { }
    }

    private static void SendForegroundKey(ushort vk)
    {
        var input = new[] { Key(vk, 0), Key(vk, KEYEVENTF_KEYUP) };
        SendInputChecked(input, "key " + vk);
    }

    private static void SendKeyDown(ushort vk) => SendInputChecked(new[] { Key(vk, 0) }, "keydown " + vk);
    private static void SendKeyUp(ushort vk) => SendInputChecked(new[] { Key(vk, KEYEVENTF_KEYUP) }, "keyup " + vk);

    private static void TypeUnicode(string text)
    {
        foreach (char c in text)
        {
            var input = new[] { Unicode(c, KEYEVENTF_UNICODE), Unicode(c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP) };
            SendInputChecked(input, "unicode character");
        }
    }

    private static void SendInputChecked(INPUT[] inputs, string context)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            int error = Marshal.GetLastWin32Error();
            throw new System.ComponentModel.Win32Exception(error, "Windows rejected the input (" + context + ", " + sent + "/" + inputs.Length + ").");
        }
    }

    private static INPUT Key(ushort vk, uint flags) => new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } } };
    private static INPUT Unicode(char c, uint flags) => new() { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = flags } } };

    private void RefreshGame()
    {
        try
        {
            if (_game != null && !_game.HasExited) return;
            var existing = Process.GetProcessesByName("How to Fish").FirstOrDefault();
            if (existing != null) { _game = existing; _patchActive = File.Exists(BackupPath); _status.Text = _patchActive ? "Game is running. Commands are available." : "Game was started outside this panel. Close it and relaunch here."; }
        }
        catch { }
    }

    private void RestoreOriginal(bool quiet)
    {
        try
        {
            if (!File.Exists(BackupPath)) { if (!quiet) _status.Text = "No backup file was found to restore."; _patchActive = false; return; }
            if (Process.GetProcessesByName("How to Fish").Any(p => !p.HasExited)) { if (!quiet) _status.Text = "Close the game before restoring the original DLL."; return; }
            File.Copy(BackupPath, AssemblyPath, true);
            File.Delete(BackupPath);
            _patchActive = false;
            _status.Text = "Original game file restored.";
        }
        catch (Exception ex) { _status.Text = "Restore failed: " + ex.Message + " The backup was preserved."; }
    }
}
