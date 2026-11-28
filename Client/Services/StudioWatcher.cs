using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Client.SmsMail.Services;

namespace Client.Services
{
    /// <summary>
    /// Surveille STUDIOV.INI (lecture seule) : quand StudioV ouvre un autre dossier, il y écrit le numéro du patient
    /// (<c>[Echange] NumPat</c>) et <see cref="PatientOpened"/> est levé. Rien ne se passe sur un poste sans StudioV.
    /// </summary>
    public sealed class StudioWatcher : IDisposable
    {
        private readonly string _path;
        private FileSystemWatcher? _watcher;
        private CancellationTokenSource? _pause;
        private (int? Patient, int? Consultation) _last;

        /// <summary>Dossier ouvert (patient, consultation) : levé quand l'un des deux change.</summary>
        public event Action<int, int?>? PatientOpened;

        public StudioWatcher(string path) { _path = path; }

        public bool Start()
        {
            var folder = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
            _last = TryRead(); // le dossier déjà ouvert au démarrage ne rouvre pas le post-it
            _watcher = new FileSystemWatcher(folder, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => Debounce();
            _watcher.Created += (_, _) => Debounce();
            _watcher.Renamed += (_, _) => Debounce();
            return true;
        }

        // StudioV réécrit le fichier en plusieurs fois : on attend qu'il ait fini avant de le lire.
        private async void Debounce()
        {
            _pause?.Cancel();
            var pause = _pause = new CancellationTokenSource();
            try { await Task.Delay(300, pause.Token); } catch (TaskCanceledException) { return; }
            (int? Patient, int? Consultation) current = (null, null);
            for (int attempt = 0; attempt < 5 && current.Patient == null; attempt++)
            {
                current = TryRead();
                if (current.Patient == null) { try { await Task.Delay(200, pause.Token); } catch (TaskCanceledException) { return; } }
            }
            if (current.Patient is not int id || current == _last) return;
            _last = current;
            PatientOpened?.Invoke(id, current.Consultation);
        }

        /// <summary>Numéro de consultation lu au démarrage (pour ne pas prendre la consultation déjà ouverte pour une nouvelle).</summary>
        public int? StartConsultation => _last.Consultation;

        private (int? Patient, int? Consultation) TryRead()
        {
            try { var text = StudioIni.Read(_path); return (StudioIni.OpenPatient(text), StudioIni.OpenConsultation(text)); }
            catch (IOException) { return (null, null); }
            catch (UnauthorizedAccessException) { return (null, null); }
        }

        public void Dispose()
        {
            _pause?.Cancel();
            _watcher?.Dispose();
        }
    }
}
