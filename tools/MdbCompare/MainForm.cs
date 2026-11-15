using System.Text;
using EyeChat.Data;

namespace MdbCompare;

public sealed class MainForm : Form
{
    private readonly TextBox path1 = PathBox(), path2 = PathBox(), workgroup = PathBox();
    private readonly TextBox user = new() { Dock = DockStyle.Fill }, password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly CheckBox compareData = new() { Text = "Comparer aussi les données (lignes)", Checked = true, AutoSize = true };
    private readonly CheckBox ignoreCase = new() { Text = "Ignorer majuscules/minuscules et espaces de fin", AutoSize = true };
    private readonly Button compare = new() { Text = "Comparer", AutoSize = true, Padding = new Padding(12, 2, 12, 2) };
    private readonly Button export = new() { Text = "Exporter le rapport…", AutoSize = true, Enabled = false };
    private readonly Button cancel = new() { Text = "Annuler", AutoSize = true, Visible = false };
    private readonly Label status = new() { AutoSize = true, Text = "Choisissez deux bases .mdb ou .accdb, puis cliquez sur Comparer.", Padding = new Padding(0, 6, 0, 0) };
    private readonly ListView tables = Grid(("Table", 220), ("État", 170), ("Lignes 1", 70), ("Lignes 2", 70), ("Différences", 85));
    private readonly ListView details = Grid(("Type", 120), ("Clé", 120), ("Détail", 900));
    private readonly CheckBox onlyDifferent = new() { Text = "Masquer les tables identiques", Checked = true, AutoSize = true, Dock = DockStyle.Top };
    private readonly Label tableInfo = new() { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(2000, 0), Padding = new Padding(2, 4, 2, 4) };

    private List<TableResult> results = new();
    private CompareOptions lastOptions = new();
    private CancellationTokenSource? running;

    public MainForm()
    {
        Text = "Comparer deux bases Access (.mdb)";
        Width = 1150; Height = 750; StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(8) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddPathRow(top, "Base 1 :", path1, "Bases Access (*.mdb;*.accdb)|*.mdb;*.accdb|Tous les fichiers|*.*");
        AddPathRow(top, "Base 2 :", path2, "Bases Access (*.mdb;*.accdb)|*.mdb;*.accdb|Tous les fichiers|*.*");
        AddPathRow(top, "Groupe de travail (.mdw, facultatif) :", workgroup, "Groupe de travail (*.mdw)|*.mdw|Tous les fichiers|*.*");

        var credentials = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Margin = Padding.Empty };
        credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        credentials.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        credentials.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        credentials.Controls.Add(user, 0, 0);
        credentials.Controls.Add(new Label { Text = "Mot de passe :", AutoSize = true, Anchor = AnchorStyles.Left }, 1, 0);
        credentials.Controls.Add(password, 2, 0);
        top.Controls.Add(new Label { Text = "Utilisateur (facultatif) :", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        top.Controls.Add(credentials, 1, 3);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        actions.Controls.AddRange(new Control[] { compare, cancel, export, compareData, ignoreCase, status });
        top.Controls.Add(actions, 0, 4);
        top.SetColumnSpan(actions, 3);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 420 };
        split.Panel1.Controls.Add(tables);
        split.Panel1.Controls.Add(onlyDifferent);
        split.Panel2.Controls.Add(details);
        split.Panel2.Controls.Add(tableInfo);

        Controls.Add(split);
        Controls.Add(top);

        compare.Click += async (_, _) => await RunAsync();
        cancel.Click += (_, _) => running?.Cancel();
        export.Click += (_, _) => Export();
        onlyDifferent.CheckedChanged += (_, _) => FillTables();
        tables.SelectedIndexChanged += (_, _) => ShowSelected();
        AcceptButton = compare;
    }

    private static TextBox PathBox()
    {
        var box = new TextBox { Dock = DockStyle.Fill, AllowDrop = true };
        box.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        box.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) box.Text = files[0]; };
        return box;
    }

    private static ListView Grid(params (string Title, int Width)[] columns)
    {
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = false };
        foreach (var (title, width) in columns) list.Columns.Add(title, width);
        return list;
    }

    private void AddPathRow(TableLayoutPanel panel, string label, TextBox box, string filter)
    {
        int row = panel.RowCount++;
        var browse = new Button { Text = "Parcourir…", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
            if (File.Exists(box.Text)) dialog.InitialDirectory = Path.GetDirectoryName(box.Text);
            if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName;
        };
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        panel.Controls.Add(box, 1, row);
        panel.Controls.Add(browse, 2, row);
    }

    private async Task RunAsync()
    {
        foreach (var (box, name) in new[] { (path1, "base 1"), (path2, "base 2") })
            if (!File.Exists(box.Text.Trim()))
            {
                MessageBox.Show(this, $"Le fichier de la {name} est introuvable.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

        var options = new CompareOptions(compareData.Checked, ignoreCase.Checked);
        AccessReadOptions Access(string path) => new(path.Trim(), "", workgroup.Text.Trim(), user.Text.Trim(), password.Text);
        var access1 = Access(path1.Text);
        var access2 = Access(path2.Text);

        running = new CancellationTokenSource();
        var token = running.Token;
        SetBusy(true);
        void Progress(string message) => BeginInvoke(() => status.Text = "Lecture : " + message);
        try
        {
            var found = await Task.Run(() =>
            {
                var db1 = AccessDatabase.Read(access1, options.CompareData, Progress, token);
                var db2 = AccessDatabase.Read(access2, options.CompareData, Progress, token);
                BeginInvoke(() => status.Text = "Comparaison…");
                return MdbComparer.Compare(db1, db2, options);
            }, token);
            results = found;
            lastOptions = options;
            FillTables();
            status.Text = Report.Summary(results);
            export.Enabled = true;
        }
        catch (OperationCanceledException) { status.Text = "Comparaison annulée."; }
        catch (Exception ex)
        {
            status.Text = "Échec de la comparaison.";
            MessageBox.Show(this, ex.Message, "Erreur de lecture Access", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            running.Dispose();
            running = null;
        }
    }

    private void SetBusy(bool busy)
    {
        compare.Enabled = !busy;
        cancel.Visible = busy;
        UseWaitCursor = busy;
    }

    private void FillTables()
    {
        tables.BeginUpdate();
        tables.Items.Clear();
        foreach (var r in results.Where(r => !onlyDifferent.Checked || r.Status != TableStatus.Identique))
        {
            var item = new ListViewItem(new[]
            {
                r.Name, Report.Label(r.Status), r.Rows1.ToString(), r.Rows2.ToString(),
                r.Status == TableStatus.Differente ? (r.DifferenceCount + r.SchemaNotes.Count).ToString() : "",
            }) { Tag = r };
            item.ForeColor = r.Status switch
            {
                TableStatus.Identique => Color.DarkGreen,
                TableStatus.Differente => Color.DarkRed,
                _ => Color.DarkBlue,
            };
            tables.Items.Add(item);
        }
        tables.EndUpdate();
        details.Items.Clear();
        tableInfo.Text = tables.Items.Count == 0 && results.Count > 0 ? "Toutes les tables sont identiques." : "";
    }

    private void ShowSelected()
    {
        details.BeginUpdate();
        details.Items.Clear();
        if (tables.SelectedItems.Count == 1 && tables.SelectedItems[0].Tag is TableResult r)
        {
            var info = new StringBuilder($"{r.Name} — {Report.Label(r.Status)}. Lignes : {r.Rows1} (base 1) / {r.Rows2} (base 2).");
            if (r.ComparedBy.Length > 0) info.Append($" Comparaison par {r.ComparedBy}.");
            if (r.DifferenceCount > 0) info.Append($" {r.OnlyIn1} seulement dans 1, {r.OnlyIn2} seulement dans 2, {r.Modified} modifiée(s).");
            if (r.DetailsTruncated) info.Append($" Seules les {lastOptions.MaxDetailsPerTable} premières différences sont listées (le rapport exporté aussi).");
            tableInfo.Text = info.ToString();
            foreach (var note in r.SchemaNotes)
                details.Items.Add(new ListViewItem(new[] { "Structure", "", note }) { ForeColor = Color.DarkOrange });
            foreach (var d in r.Details)
                details.Items.Add(new ListViewItem(new[] { Report.Label(d.Kind), d.Key, d.Detail })
                {
                    ForeColor = d.Kind switch { RowChange.SeulementDans1 => Color.DarkRed, RowChange.SeulementDans2 => Color.DarkGreen, _ => Color.DarkBlue },
                });
        }
        details.EndUpdate();
    }

    private void Export()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Rapport texte (*.txt)|*.txt|Tableau CSV pour Excel (*.csv)|*.csv",
            FileName = $"comparaison-{DateTime.Now:yyyy-MM-dd-HHmm}.txt",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var csv = dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        // UTF-8 avec BOM pour qu'Excel affiche correctement les accents.
        File.WriteAllText(dialog.FileName,
            csv ? Report.ToCsv(results) : Report.ToText(path1.Text.Trim(), path2.Text.Trim(), results, lastOptions),
            new UTF8Encoding(true));
        status.Text = "Rapport enregistré : " + dialog.FileName;
    }
}
