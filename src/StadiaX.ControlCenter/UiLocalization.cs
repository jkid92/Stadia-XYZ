using System.Globalization;
using System.Text.Json;

namespace StadiaX.ControlCenter;

internal sealed class UiLocalization
{
    private static readonly IReadOnlyDictionary<string, string> Italian = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Home"] = "Home",
        ["Controllers"] = "Controller",
        ["Test"] = "Test",
        ["Logs"] = "Log",
        ["Support"] = "Supporto",
        ["Doctor"] = "Diagnosi",
        ["Pairing"] = "Associazione",
        ["Bridge"] = "Bridge",
        ["Devices"] = "Dispositivi",
        ["Profiles"] = "Profili",
        ["Macros"] = "Macro",
        ["Settings"] = "Impostazioni",
        ["Checks"] = "Controlli",
        ["Control"] = "Controllo",
        ["Controller service"] = "Servizio controller",
        ["Control center"] = "Centro di controllo",
        ["Automatic virtual controller"] = "Controller virtuale automatico",
        ["Bluetooth controller bridge"] = "Bridge Bluetooth per controller",
        ["Current operation"] = "Operazione corrente",
        ["Recent user actions"] = "Azioni recenti",
        ["Controller connection"] = "Connessione controller",
        ["Detected Stadia controllers"] = "Controller Stadia rilevati",
        ["Connection activity"] = "Attività di connessione",
        ["Visual controller test"] = "Test visivo controller",
        ["Telemetry"] = "Telemetria",
        ["Live logs"] = "Log in tempo reale",
        ["Windows Native timeline"] = "Cronologia Windows Native",
        ["Status timeline"] = "Cronologia stato",
        ["Linux core"] = "Nucleo Linux",
        ["User actions"] = "Azioni utente",
        ["App diagnostics"] = "Diagnostica app",
        ["Diagnostics"] = "Diagnostica",
        ["Guided pairing"] = "Associazione guidata",
        ["Linux devices"] = "Dispositivi Linux",
        ["Readiness checklist"] = "Controlli di preparazione",
        ["Controller Doctor"] = "Diagnosi controller",
        ["Bridge actions"] = "Azioni bridge",
        ["Current selection and battery"] = "Selezione e batteria",
        ["WSL distro"] = "Distribuzione WSL",
        ["Bluetooth BUSID"] = "BUSID Bluetooth",
        ["USB/IP devices"] = "Dispositivi USB/IP",
        ["Requirement checks"] = "Controllo requisiti",
        ["Windows Bluetooth"] = "Bluetooth Windows",
        ["Visible to Linux"] = "Visibili a Linux",
        ["Saved controllers"] = "Controller salvati",
        ["Profile editor"] = "Editor profilo",
        ["Macro editor"] = "Editor macro",
        ["Start"] = "Avvia",
        ["Start bridge"] = "Avvia bridge",
        ["Start native"] = "Avvia nativo",
        ["Stop"] = "Ferma",
        ["Stop and restore"] = "Ferma e ripristina",
        ["Stop native"] = "Ferma nativo",
        ["Refresh"] = "Aggiorna",
        ["Refresh all"] = "Aggiorna",
        ["Refresh setup"] = "Aggiorna configurazione",
        ["Self-test"] = "Autotest",
        ["Run doctor"] = "Avvia diagnosi",
        ["Scan"] = "Cerca",
        ["Scan devices"] = "Cerca dispositivi",
        ["Repair"] = "Ripara",
        ["Bundle"] = "Pacchetto log",
        ["Releases"] = "Versioni",
        ["Check"] = "Controlla",
        ["Check controllers"] = "Controlla controller",
        ["Check updates"] = "Controlla aggiornamenti",
        ["Rollback"] = "Ripristina",
        ["Test input"] = "Prova input",
        ["Connection details"] = "Dettagli connessione",
        ["Pair"] = "Associa",
        ["Connect"] = "Connetti",
        ["Disconnect"] = "Disconnetti",
        ["Use selected"] = "Usa selezionati",
        ["Automatic"] = "Automatico",
        ["Enable"] = "Abilita",
        ["Disable"] = "Disabilita",
        ["Capacity"] = "Capacità",
        ["Use"] = "Usa",
        ["Save"] = "Salva",
        ["Reload"] = "Ricarica",
        ["Open folder"] = "Apri cartella",
        ["Open logs"] = "Apri log",
        ["Open in Notepad"] = "Apri in Blocco note",
        ["Session report"] = "Rapporto sessione",
        ["Support bundle"] = "Pacchetto assistenza",
        ["Probe"] = "Analizza",
        ["Show"] = "Mostra",
        ["Exit"] = "Esci",
        ["Ready"] = "Pronto",
        ["Waiting"] = "In attesa",
        ["Waiting for controller"] = "In attesa del controller",
        ["Connect a Stadia controller to continue"] = "Connetti un controller Stadia per continuare",
        ["Connect a Stadia controller, then press Start. Virtual controller setup is automatic."] = "Connetti un controller Stadia e premi Avvia. La configurazione del controller virtuale è automatica.",
        ["Start the bridge or open the pairing wizard."] = "Avvia il bridge oppure apri l'associazione guidata.",
        ["No active request"] = "Nessuna operazione in corso",
        ["No profile"] = "Nessun profilo",
        ["Virtual pad waiting"] = "Controller virtuale in attesa",
        ["Automatic mapping"] = "Mappatura automatica",
        ["User action log not loaded yet."] = "Log azioni utente non ancora caricato.",
        ["Status log not loaded yet."] = "Log di stato non ancora caricato.",
        ["Linux log not loaded yet."] = "Log Linux non ancora caricato.",
        ["Windows Native log not loaded yet."] = "Log Windows Native non ancora caricato.",
        ["App diagnostics log not loaded yet."] = "Log diagnostica app non ancora caricato.",
        ["Run self-test, update check, session report, or support bundle to see output here."] = "Avvia un test, controlla gli aggiornamenti o crea un pacchetto assistenza per vedere qui il risultato.",
        ["Run Doctor to check bridge readiness"] = "Avvia il controllo per verificare la preparazione",
        ["Waiting for setup data"] = "In attesa dei dati di configurazione",
        ["Selected: none"] = "Selezione: nessuna",
        ["Battery overlay"] = "Overlay batteria",
        ["Use at startup"] = "Usa all'avvio",
        ["Apply chord"] = "Applica combinazione",
        ["Step"] = "Fase",
        ["State"] = "Stato",
        ["Details"] = "Dettagli",
        ["Name"] = "Nome",
        ["Status"] = "Stato",
        ["Instance ID"] = "ID istanza",
        ["Item"] = "Elemento",
        ["Bluetooth"] = "Bluetooth",
        ["Battery"] = "Batteria",
        ["Paired"] = "Associato",
        ["Trust"] = "Autorizzato",
        ["Source"] = "Origine",
        ["Slot"] = "Posto",
        ["Auto"] = "Auto",
        ["Controller"] = "Controller",
        ["Hardware"] = "Hardware",
        ["Input"] = "Input",
        ["Protected"] = "Protetto",
        ["Device path"] = "Percorso dispositivo",
        ["On"] = "Attivo",
        ["Pressed"] = "Premuti",
        ["Chord"] = "Combinazione",
        ["Shortcut"] = "Scorciatoia",
        ["Controller profile"] = "Profilo controller",
        ["Macro editor"] = "Editor macro",
        ["Select one or more Linux Bluetooth devices first."] = "Seleziona prima uno o più dispositivi Bluetooth Linux.",
        ["Select a Linux Bluetooth device first."] = "Seleziona prima un dispositivo Bluetooth Linux.",
        ["Select a Windows Bluetooth device first."] = "Seleziona prima un dispositivo Bluetooth Windows.",
        ["Choose a chord and type a shortcut first."] = "Scegli prima una combinazione e inserisci una scorciatoia.",
        ["This receiver row does not expose a Bluetooth MAC yet. Use Refresh or Scan until the BlueZ row appears, then save the profile."] = "Il ricevitore non mostra ancora un MAC Bluetooth. Premi Aggiorna o Cerca finché compare la riga BlueZ, quindi salva il profilo.",
        ["StadiaX.exe was not found. Build or install the native launcher first."] = "StadiaX.exe non è stato trovato. Installa prima il programma completo.",
        ["Up to date"] = "Aggiornato",
        ["Update check failed"] = "Controllo aggiornamenti non riuscito",
        ["Stadia X update"] = "Aggiornamento Stadia X",
        ["No previous version is available."] = "Non è disponibile una versione precedente.",
        ["Telemetry read failed"] = "Lettura telemetria non riuscita"
    };

    private static readonly (string English, string Italian)[] Prefixes = { ("Version ", "Versione "), ("Battery: ", "Batteria: "), ("Selected: ", "Selezione: "), ("Update available: ", "Aggiornamento disponibile: "), ("Linux devices: ", "Dispositivi Linux: "), ("Installed: ", "Installata: "), ("Latest: ", "Più recente: "), ("Failed - ", "Errore - "), ("Input ", "Input ") };
    private readonly HashSet<Control> _attachedControls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, string> _controlSources = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ColumnHeader, string> _columnSources = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ToolStripItem, string> _menuSources = new(ReferenceEqualityComparer.Instance);
    private bool _applying;

    internal UiLocalization() => LanguageCode = LoadLanguageCode();
    internal string LanguageCode { get; private set; }
    internal bool IsItalian => LanguageCode.Equals("it", StringComparison.OrdinalIgnoreCase);

    internal void SetLanguage(string languageCode)
    {
        var normalized = languageCode.Equals("it", StringComparison.OrdinalIgnoreCase) ? "it" : "en";
        if (LanguageCode.Equals(normalized, StringComparison.OrdinalIgnoreCase)) return;
        LanguageCode = normalized;
        SaveLanguageCode(normalized);
    }

    internal string Translate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? "";
        if (!IsItalian) return text;
        if (Italian.TryGetValue(text, out var translated)) return translated;
        foreach (var prefix in Prefixes) if (text.StartsWith(prefix.English, StringComparison.Ordinal)) return prefix.Italian + text[prefix.English.Length..];
        return text;
    }

    internal void Apply(Control root)
    {
        _applying = true;
        try { ApplyControl(root); } finally { _applying = false; }
    }

    internal void Apply(ContextMenuStrip? menu)
    {
        if (menu is null) return;
        foreach (ToolStripItem item in menu.Items) ApplyToolStripItem(item);
    }

    private void ApplyControl(Control control)
    {
        if (ShouldTranslateText(control))
        {
            if (!_controlSources.TryGetValue(control, out var source))
            {
                source = control.Text;
                _controlSources[control] = source;
            }
            control.Text = Translate(source);
            if (control is not TextBox && _attachedControls.Add(control)) control.TextChanged += TranslateChangedControl;
        }
        if (control is ListView list)
        {
            foreach (ColumnHeader column in list.Columns)
            {
                if (!_columnSources.TryGetValue(column, out var source))
                {
                    source = column.Text;
                    _columnSources[column] = source;
                }
                column.Text = Translate(source);
            }
        }
        foreach (Control child in control.Controls) ApplyControl(child);
    }

    private void TranslateChangedControl(object? sender, EventArgs e)
    {
        if (_applying || sender is not Control control) return;
        _controlSources[control] = control.Text;
        var translated = Translate(control.Text);
        if (translated.Equals(control.Text, StringComparison.Ordinal)) return;
        _applying = true;
        try { control.Text = translated; } finally { _applying = false; }
    }

    private static bool ShouldTranslateText(Control control) => control is Form or Label or Button or CheckBox or RadioButton or GroupBox or TabPage or ModernTabButton || control is TextBox { ReadOnly: true } textBox && textBox.TextLength < 400 && !textBox.Text.Contains('|');
    private void ApplyToolStripItem(ToolStripItem item)
    {
        if (!_menuSources.TryGetValue(item, out var source))
        {
            source = item.Text ?? "";
            _menuSources[item] = source;
        }
        item.Text = Translate(source);
        if (item is ToolStripDropDownItem menu) foreach (ToolStripItem child in menu.DropDownItems) ApplyToolStripItem(child);
    }

    private static string LoadLanguageCode()
    {
        var previewLanguage = Environment.GetEnvironmentVariable("STADIAX_UI_LANGUAGE");
        if (previewLanguage is "it" or "en") return previewLanguage;

        try
        {
            if (File.Exists(SettingsPath))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (json.RootElement.TryGetProperty("language", out var value))
                {
                    var code = value.GetString();
                    if (code is "it" or "en") return code;
                }
            }
        }
        catch
        {
        }
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("it", StringComparison.OrdinalIgnoreCase) ? "it" : "en";
    }

    private static void SaveLanguageCode(string languageCode)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); var temporaryPath = SettingsPath + ".tmp"; File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new { language = languageCode }, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporaryPath, SettingsPath, overwrite: true); } catch { }
    }

    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stadia X", "ui-settings.json");
    internal static UiLocalization Current { get; } = new();
}
