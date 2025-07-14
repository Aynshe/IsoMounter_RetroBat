using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Management;
using Microsoft.Win32;

class Program
{
    // Chemin d'installation de WinCDEmu
    private static string _winCdEmuPath = null;
    private static readonly string[] _supportedImageFormats = { ".iso", ".bin", ".cue", ".img", ".mdf", ".nrg", ".cdi", ".dmg" };
    private static readonly string[] _winCdEmuExecutables = { "WinCDEmu.exe", "vmnt.exe", "vmnt64.exe" };

    // Fichier pour stocker le chemin de l'image montée / File to store the mounted image path
    private static readonly string MountInfoFile = Path.Combine(Path.GetTempPath(), "IsoMounter.mount");
    // Chemin du fichier de log / Log file path
    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IsoMounter.log");

    // Constantes pour le montage d'images disque
    private const int DDD_RAW_TARGET_PATH = 0x1;
    private const int DDD_REMOVE_DEFINITION = 0x2;
    private const int DONT_RESOLVE_DLL_REFERENCES = 0x00000001;
    
    // Importation des fonctions Windows nécessaires
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DefineDosDevice(int dwFlags, string lpDeviceName, string lpTargetPath);
    
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetDriveType(string lpRootPathName);
    
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string lpszVolumeMountPoint, 
                                                             [Out] StringBuilder lpszVolumeName, 
                                                             uint cchBufferLength);

    // Sauvegarde le chemin de l'image montée dans un fichier temporaire
    // Saves the mounted image path to a temporary file
    private static void SaveMountedImage(string imagePath)
    {
        File.WriteAllText(MountInfoFile, imagePath, Encoding.UTF8);
    }

    // Récupère le chemin de l'image actuellement montée
    // Gets the path of the currently mounted image
    private static string GetMountedImagePath()
    {
        if (File.Exists(MountInfoFile))
        {
            string path = File.ReadAllText(MountInfoFile, Encoding.UTF8).Trim();
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    // Supprime le fichier d'information de montage
    // Removes the mount information file
    private static void ClearMountedImage()
    {
        if (File.Exists(MountInfoFile))
        {
            File.Delete(MountInfoFile);
        }
    }

    // Enregistre un message dans le journal et la console
    // Logs a message to both file and console
    private static void LogMessage(string message, bool isError = false)
    {
        try
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            string logMessage = $"[{timestamp}] {(isError ? "ERREUR" : "INFO")} - {message}";
            
            // Écrire dans la console / Write to console
            Console.WriteLine(logMessage);
            
            // Écrire dans le fichier log / Write to log file
            File.AppendAllText(LogFilePath, logMessage + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur lors de l'écriture dans le journal: {ex.Message}");
        }
    }
    
    // Démonte l'image précédemment montée
    // Unmounts the previously mounted image
    private static int UnmountImage()
    {
        string mountedImage = GetMountedImagePath();
        if (mountedImage == null)
        {
            LogMessage("Aucune image montée trouvée / No mounted image found");
            return 1;
        }

        LogMessage($"Début du démontage de l'image / Starting to unmount image: {mountedImage}");
        Console.WriteLine($"Démontage de l'image / Unmounting image: {mountedImage}");
        
        try
        {
            // Vérifier si WinCDEmu est disponible
            bool hasWinCDEmu = IsWinCDEmuInstalled();
            bool success = false;
            
            if (hasWinCDEmu)
            {
                // Utiliser WinCDEmu pour tout type d'image
                LogMessage("Utilisation de WinCDEmu pour le démontage / Using WinCDEmu for unmounting");
                success = UnmountWithWinCDEmu(mountedImage);
            }
            else
            {
                // Vérifier si c'est une ISO pour le démontage natif Windows
                string extension = Path.GetExtension(mountedImage)?.ToLowerInvariant();
                bool isIso = extension == ".iso";
                
                if (isIso)
                {
                    // Utiliser le démontage natif Windows uniquement pour les ISO
                    LogMessage("Utilisation du démontage natif Windows pour l'ISO / Using native Windows unmount for ISO");
                    success = UnmountWithWindowsNative(mountedImage);
                }
                else
                {
                    LogMessage("Aucune méthode de démontage disponible pour ce format d'image / No unmount method available for this image format", true);
                    return 1;
                }
            }

            if (success)
            {
                ClearMountedImage();
                LogMessage("Démontage réussi / Unmount successful");
                return 0;
            }
            
            LogMessage("Échec du démontage avec toutes les méthodes / Failed to unmount with all methods", true);
            return 1;
        }
        catch (Exception ex)
        {
            LogMessage($"Erreur lors du démontage / Error while unmounting: {ex.Message}", true);
            return 1;
        }
    }

    // Démonte une image avec la méthode native Windows (pour les ISO)
    private static bool UnmountWithWindowsNative(string imagePath)
    {
        try
        {
            LogMessage("Tentative de démontage natif Windows / Trying native Windows unmount...");
            
            string psCommand = $"Dismount-DiskImage -ImagePath '{imagePath.Replace("'", "''")}'";
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);

                if (!string.IsNullOrEmpty(output)) LogMessage($"Sortie native: {output}");
                if (!string.IsNullOrEmpty(error)) LogMessage($"Erreur native: {error}", true);

                return process.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Erreur lors du démontage natif / Error during native unmount: {ex.Message}", true);
            return false;
        }
    }

    // Démonte une image avec WinCDEmu
    private static bool UnmountWithWinCDEmu(string imagePath)
    {
        try
        {
            if (string.IsNullOrEmpty(_winCdEmuPath))
            {
                LogMessage("WinCDEmu n'est pas installé / WinCDEmu is not installed");
                return false;
            }

            string winCdEmuDir = Path.GetDirectoryName(_winCdEmuPath);
            string batchMntPath = Path.Combine(winCdEmuDir, "batchmnt64.exe");
            
            if (!File.Exists(batchMntPath))
            {
                batchMntPath = Path.Combine(winCdEmuDir, "batchmnt.exe");
                if (!File.Exists(batchMntPath))
                {
                    LogMessage("Aucun exécutable batchmnt trouvé pour le démontage.", true);
                    return false;
                }
            }

            // Utiliser la syntaxe exacte qui fonctionne en ligne de commande
            string arguments = $"/unmount \"{imagePath}\"";
            LogMessage($"Exécution de la commande: {Path.GetFileName(batchMntPath)} {arguments}");
            
            var startInfo = new ProcessStartInfo
            {
                FileName = batchMntPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = winCdEmuDir
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();
                
                process.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        outputBuilder.AppendLine(e.Data);
                        LogMessage($"Sortie: {e.Data}");
                    }
                };
                
                process.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        errorBuilder.AppendLine(e.Data);
                        LogMessage($"Erreur: {e.Data}", true);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                bool success = process.WaitForExit(10000);
                string output = outputBuilder.ToString();
                string error = errorBuilder.ToString();
                
                if (!success || process.ExitCode != 0)
                {
                    LogMessage($"Erreur lors du démontage. Code de sortie: {process.ExitCode}", true);
                    if (!string.IsNullOrEmpty(output)) LogMessage($"Sortie complète: {output}", true);
                    if (!string.IsNullOrEmpty(error)) LogMessage($"Erreur complète: {error}", true);
                    return false;
                }
                
                return true;
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Erreur lors du démontage WinCDEmu: {ex.Message}", true);
            return false;
        }
    }

    // Vérifie si une image est déjà montée
    // Checks if an image is already mounted
    private static bool IsImageMounted(string imagePath)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -NoProfile -NonInteractive -Command \"& {{ (Get-DiskImage -ImagePath '{imagePath.Replace("'", "''")}').Attached }}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return output.Equals("True", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // Détecte si WinCDEmu est installé et retourne le chemin d'installation
    private static bool IsWinCDEmuInstalled()
    {
        try
        {
            // Vérifier dans le registre Windows
            string[] registryPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WinCDEmu",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\WinCDEmu"
            };

            foreach (var registryPath in registryPaths)
            {
                using (var key = Registry.LocalMachine.OpenSubKey(registryPath))
                {
                    if (key != null)
                    {
                        string installPath = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(installPath))
                        {
                            foreach (var exe in _winCdEmuExecutables)
                            {
                                string fullPath = Path.Combine(installPath, exe);
                                if (File.Exists(fullPath))
                                {
                                    _winCdEmuPath = fullPath;
                                    LogMessage($"WinCDEmu trouvé: {_winCdEmuPath}");
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            // Vérifier dans les dossiers Program Files
            string[] programFilesPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinCDEmu"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WinCDEmu")
            };

            foreach (var path in programFilesPaths.Distinct())
            {
                if (Directory.Exists(path))
                {
                    foreach (var exe in _winCdEmuExecutables)
                    {
                        string fullPath = Path.Combine(path, exe);
                        if (File.Exists(fullPath))
                        {
                            _winCdEmuPath = fullPath;
                            LogMessage($"WinCDEmu trouvé dans Program Files: {_winCdEmuPath}");
                            return true;
                        }
                    }
                }
            }

            // Vérifier dans le PATH système
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var path in pathEnv.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    foreach (var exe in _winCdEmuExecutables)
                    {
                        string fullPath = Path.Combine(path, exe);
                        if (File.Exists(fullPath))
                        {
                            _winCdEmuPath = fullPath;
                            LogMessage($"WinCDEmu trouvé dans le PATH: {_winCdEmuPath}");
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Erreur lors de la détection de WinCDEmu: {ex.Message}", true);
        }

        LogMessage("WinCDEmu non trouvé sur le système", true);
        return false;
    }

    // Monte une image avec WinCDEmu en utilisant batchmnt64.exe ou batchmnt.exe
    private static bool MountWithWinCDEmu(string imagePath)
    {
        try
        {
            string winCdEmuDir = Path.GetDirectoryName(_winCdEmuPath);
            
            // Essayer d'abord batchmnt64.exe, puis batchmnt.exe
            string batchMntPath = Path.Combine(winCdEmuDir, "batchmnt64.exe");
            if (!File.Exists(batchMntPath))
            {
                batchMntPath = Path.Combine(winCdEmuDir, "batchmnt.exe");
                if (!File.Exists(batchMntPath))
                {
                    LogMessage("Aucun exécutable batchmnt trouvé dans le dossier WinCDEmu.", true);
                    return false;
                }
            }

            // Monter l'image avec batchmnt et l'option /wait
            var startInfo = new ProcessStartInfo
            {
                FileName = batchMntPath,
                Arguments = $@"""{imagePath}"" /wait",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = winCdEmuDir
            };

            LogMessage($"Exécution de: {Path.GetFileName(batchMntPath)} {startInfo.Arguments}");

            using (var process = new Process { StartInfo = startInfo })
            {
                // Lire la sortie de manière asynchrone pour éviter les deadlocks
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();
                
                process.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        outputBuilder.AppendLine(e.Data);
                        LogMessage($"Sortie: {e.Data}");
                    }
                };
                
                process.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        errorBuilder.AppendLine(e.Data);
                        LogMessage($"Erreur: {e.Data}", true);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                bool success = process.WaitForExit(30000); // Attendre max 30 secondes
                
                if (!success)
                {
                    LogMessage("Le processus de montage a pris trop de temps.", true);
                    return false;
                }
                
                string output = outputBuilder.ToString();
                string error = errorBuilder.ToString();
                
                if (process.ExitCode != 0)
                {
                    LogMessage($"Erreur lors du montage avec WinCDEmu. Code de sortie: {process.ExitCode}", true);
                    if (!string.IsNullOrEmpty(output)) LogMessage($"Sortie complète: {output}", true);
                    if (!string.IsNullOrEmpty(error)) LogMessage($"Erreur complète: {error}", true);
                    return false;
                }
                
                LogMessage("Image montée avec succès via WinCDEmu");
                return true;
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Erreur lors du montage avec WinCDEmu: {ex.Message}", true);
            return false;
        }
    }

    // Montage d'une image ISO avec PowerShell
    // Mount an ISO image using PowerShell
    private static bool MountIso(string isoPath)
    {
        try
        {
            // Vérifier si le fichier existe / Check if file exists
            if (!File.Exists(isoPath))
            {
                LogMessage($"Le fichier ISO n'existe pas / ISO file does not exist: {isoPath}", true);
                return false;
            }

            // Vérifier si l'image est déjà montée
            // Check if image is already mounted
            if (IsImageMounted(isoPath))
            {
                LogMessage("L'image est déjà montée / Image is already mounted");
                return true;
            }

            // Préparer la commande PowerShell
            // Prepare PowerShell command
            string psCommand = $"$null = Mount-DiskImage -ImagePath '{isoPath.Replace("'", "''")}' -PassThru -ErrorAction Stop";
            
            LogMessage($"Début du montage / Starting mount process...");
            
            var stopwatch = Stopwatch.StartNew();
            
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -NoProfile -NonInteractive -Command \"& {{ {psCommand} }}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Start();
                
                // Attendre un court instant pour laisser le temps au montage de commencer
                // Wait a bit to allow mounting to start
                if (!process.WaitForExit(5000)) // 5 secondes maximum
                {
                    LogMessage("Le montage prend plus de temps que prévu / Mounting is taking longer than expected");
                }
                
                process.WaitForExit();
                stopwatch.Stop();
                
                LogMessage($"Temps de montage / Mounting time: {stopwatch.ElapsedMilliseconds}ms");

                if (process.ExitCode != 0)
                {
                    LogMessage($"Échec du montage avec le code de sortie / Mounting failed with exit code: {process.ExitCode}", true);
                    return false;
                }

                // Vérifier que le montage a bien eu lieu
                // Verify the mount was successful
                int retries = 10; // Nombre de tentatives de vérification / Number of verification attempts
                while (retries-- > 0)
                {
                    if (IsImageMounted(isoPath))
                    {
                        LogMessage("Image montée avec succès / Image mounted successfully");
                        return true;
                    }
                    Thread.Sleep(100); // Attendre 100ms entre chaque vérification / Wait 100ms between checks
                }

                LogMessage("Le montage semble avoir réussi mais n'est pas encore visible / Mounting seems successful but not yet visible", true);
                return true;
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Erreur lors du montage de l'ISO / Error mounting ISO: {ex.Message}", true);
            return false;
        }
    }

    // Initialise le système de journalisation
    // Initializes the logging system
    private static void InitializeLog()
    {
        try
        {
            // Créer le fichier de log ou l'écraser s'il existe déjà
            // Create log file or overwrite if it exists
            File.WriteAllText(LogFilePath, "", Encoding.UTF8);
            LogMessage("Démarrage de l'application / Application starting");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur lors de l'initialisation du journal / Error initializing log: {ex.Message}");
        }
    }

    // Point d'entrée principal de l'application / Main application entry point
    static int Main(string[] args)
    {
        InitializeLog();
        
        // Vérification des arguments / Arguments validation
        if (args.Length == 0)
        {
            string errorMessage = "Aucun argument fourni. Utilisation: Montage: IsoMounter.exe \"chemin/vers/rom\" ou Démontage: IsoMounter.exe --unmount";
            string errorMessageEn = "No arguments provided. Usage: Mount: IsoMounter.exe \"path/to/rom\" or Unmount: IsoMounter.exe --unmount";
            LogMessage($"{errorMessage} | {errorMessageEn}", true);
            Console.WriteLine($"Erreur / Error: {errorMessage}");
            Console.WriteLine("Utilisation / Usage:");
            Console.WriteLine("  Montage / Mount: IsoMounter.exe \"chemin/vers/rom\"");
            Console.WriteLine("  Démontage / Unmount: IsoMounter.exe --unmount");
            return 1;
        }

        // Mode démontage / Unmount mode
        if (args[0].Equals("--unmount", StringComparison.OrdinalIgnoreCase))
        {
            return UnmountImage();
        }

        // Par défaut, mode non-interactif pour une utilisation avec RetroBat
        // Default to non-interactive mode for RetroBat usage
        bool interactive = args.Any(a => a == "--interactive");
        
        // Filtrer les arguments pour ne garder que le chemin du jeu
        // Filter arguments to keep only the game path and relevant options
        string[] filteredArgs = args.Where(a => a != "--interactive").ToArray();

        try
        {
            // Reconstruire le chemin complet correctement en tenant compte des guillemets
            // Rebuild the full path correctly handling quotes
            string fullPath = string.Join(" ", filteredArgs);
            LogMessage($"Valeur brute des arguments / Raw arguments value: {fullPath}");
            
            // Le premier argument est le chemin complet du jeu, potentiellement avec des espaces
            // First argument is the full path to the game, potentially with spaces
            string romPath = fullPath.Trim();
            
            // Nettoyer le chemin des guillemets s'ils sont présents
            // Clean path from quotes if present
            romPath = romPath.Trim('"');
            LogMessage($"Chemin ROM nettoyé / Cleaned ROM path: {romPath}");
            
            // Extraire le nom du jeu à partir du chemin complet
            // Extract game name from full path
            
            // Extraire le nom du jeu (dernier segment du chemin, sans extension)
            // Extract game name (last path segment without extension)
            string gameName = Path.GetFileName(romPath);
            LogMessage($"Nom du fichier extrait / Extracted filename: {gameName}");
            
            // Si le chemin contient 'roms' (insensible à la casse), on prend le segment suivant
            // If path contains 'roms' (case insensitive), take the following segment
            int romsIndex = romPath.IndexOf("roms", StringComparison.OrdinalIgnoreCase);
            if (romsIndex >= 0)
            {
                string afterRoms = romPath.Substring(romsIndex + 4); // +4 pour la longueur de "roms" / +4 for "roms" length
                LogMessage($"Chemin après 'roms' / Path after 'roms': {afterRoms}");
                
                // Nettoyer et diviser le chemin
                var pathParts = afterRoms.Trim('\\', '/', ' ').Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                
                // Si on a au moins une partie de chemin après 'roms', on la prend comme nom de jeu
                if (pathParts.Length > 0)
                {
                            // Si le chemin contient 'steam', on prend le nom du fichier complet
                    // If path contains 'steam', take the full filename
                    if (afterRoms.Trim('\\', '/').StartsWith("steam", StringComparison.OrdinalIgnoreCase))
                    {
                        // Pour les jeux Steam, on prend le nom du fichier complet (sans extension)
                        // For Steam games, take the full filename (without extension)
                        string fileName = Path.GetFileName(romPath);
                        gameName = Path.GetFileNameWithoutExtension(fileName);
                    }
                    else if (pathParts.Length > 0)
                    {
                        // Pour les autres cas, on prend le dernier segment du chemin
                        // For other cases, take the last path segment
                        gameName = pathParts[pathParts.Length - 1];
                    }
                    
                    // Supprimer l'extension si présente
                    gameName = Path.GetFileNameWithoutExtension(gameName);
                    LogMessage($"Nom du jeu extrait / Extracted game name: {gameName}");
                }
            }

            LogMessage($"Recherche de l'image pour le jeu / Searching image for game: {gameName}");
            Console.WriteLine($"Recherche de l'image pour le jeu / Searching image for game: {gameName}");

            // Dossier contenant les images ISO (même dossier que l'exécutable)
            // ISO images folder (same directory as executable)
            string appPath = AppDomain.CurrentDomain.BaseDirectory;
            string isoFolder = Path.Combine(appPath, "iso");
            LogMessage($"Dossier des images / Images folder: {isoFolder}");

            // Créer le dossier s'il n'existe pas
            // Create directory if it doesn't exist
            if (!Directory.Exists(isoFolder))
            {
                try
                {
                    LogMessage("Création du dossier ISO car il n'existe pas / Creating ISO folder as it doesn't exist");
                    Directory.CreateDirectory(isoFolder);
                    string message = $"Le dossier {isoFolder} a été créé. Veuillez y placer vos images disque.\n" +
                                    $"The folder {isoFolder} has been created. Please place your disk images there.";
                    LogMessage(message);
                    Console.WriteLine(message);
                    return 1;
                }
                catch (Exception ex)
                {
                    string errorMsg = $"Impossible de créer le dossier / Could not create folder {isoFolder}: {ex.Message}";
                    LogMessage(errorMsg, true);
                    Console.WriteLine(errorMsg);
                    return 1;
                }
            }

            // Vérifier si WinCDEmu est installé
            bool canUseWinCDEmu = IsWinCDEmuInstalled();
            
            // Définir les formats supportés en fonction de la disponibilité de WinCDEmu
            var supportedFormats = canUseWinCDEmu 
                ? _supportedImageFormats  // Tous les formats si WinCDEmu est installé
                : new[] { ".iso" };      // Seulement ISO si WinCDEmu n'est pas installé
            
            if (!canUseWinCDEmu)
            {
                LogMessage("WinCDEmu n'est pas installé. Seul le format ISO est supporté.", true);
            }
            else
            {
                LogMessage("WinCDEmu est installé. Tous les formats d'image sont supportés.");
            }

            // Chercher un fichier d'image correspondant au nom du jeu
            // Search for image file matching the game name
            var searchPatterns = supportedFormats.SelectMany(f => new[] 
                { 
                    $"{gameName}{f}",
                    $"{gameName} (Disc 1){f}",
                    $"{gameName} (Disc 2){f}",
                    $"{gameName} (Disc 3){f}",
                    $"{gameName} (Disc 4){f}",
                    $"Disc 1 of {gameName}{f}",
                    $"Disc 2 of {gameName}{f}",
                    $"Disc 3 of {gameName}{f}",
                    $"Disc 4 of {gameName}{f}"
                });
                
            // Pour les fichiers .cue, on vérifie aussi le .bin correspondant
            if (canUseWinCDEmu && searchPatterns.Any(p => p.EndsWith(".cue")))
            {
                searchPatterns = searchPatterns.Concat(searchPatterns
                    .Where(p => p.EndsWith(".cue"))
                    .Select(p => p.Replace(".cue", ".bin")));
            }

            string[] matchingFiles = searchPatterns
                .SelectMany(pattern => Directory.GetFiles(isoFolder, pattern, SearchOption.TopDirectoryOnly))
                .ToArray();

            if (matchingFiles.Length == 0)
            {
                string errorMsg = $"Aucune image trouvée pour le jeu / No image found for game: {gameName}";
                LogMessage(errorMsg, true);
                Console.WriteLine(errorMsg);
                return 1;
            }

            string imagePath = matchingFiles[0];
            LogMessage($"Image trouvée / Image found: {imagePath}");
            Console.WriteLine($"Image trouvée / Image found: {imagePath}");

            // Essayer de monter l'image
            try
            {
                bool mountSuccess = false;
                string extension = Path.GetExtension(imagePath).ToLowerInvariant();

                // Si WinCDEmu est disponible, l'utiliser pour tous les types d'images
                if (canUseWinCDEmu)
                {
                    // Pour les .bin, on vérifie s'il y a un .cue correspondant
                    if (extension == ".bin")
                    {
                        string cuePath = Path.ChangeExtension(imagePath, ".cue");
                        if (File.Exists(cuePath))
                        {
                            LogMessage($"Fichier .cue trouvé, utilisation de celui-ci: {cuePath}");
                            imagePath = cuePath;
                        }
                    }
                    
                    LogMessage($"Tentative de montage avec WinCDEmu: {imagePath}");
                    mountSuccess = MountWithWinCDEmu(imagePath);
                }
                // Sinon, utiliser le montage natif Windows uniquement pour les ISO
                else if (extension == ".iso")
                {
                    LogMessage($"Tentative de montage natif Windows: {imagePath}");
                    mountSuccess = MountIso(imagePath);
                }
                else
                {
                    string errorMsg = "Le format de l'image n'est pas pris en charge sans WinCDEmu. / Image format not supported without WinCDEmu.";
                    LogMessage(errorMsg, true);
                    Console.WriteLine(errorMsg);
                    return 1;
                }

                if (mountSuccess)
                {
                        // Enregistrer le chemin de l'image montée
                        // Save the mounted image path
                        try
                        {
                            SaveMountedImage(imagePath);
                            LogMessage("Information de montage enregistrée / Mount information saved");
                        }
                        catch (Exception ex)
                        {
                            string errorMsg = $"Erreur lors de l'enregistrement de l'état de montage / Error saving mount state: {ex.Message}";
                            LogMessage(errorMsg, true);
                            Console.WriteLine(errorMsg);
                        }
                        
                        // En mode non-interactif (par défaut), on quitte immédiatement
                        LogMessage("Mode non-interactif, sortie immédiate / Non-interactive mode, exiting immediately");
                        
                        // Mode interactif uniquement si explicitement demandé
                        if (interactive && Environment.UserInteractive)
                        {
                            LogMessage("Mode interactif détecté, attente d'une touche... / Interactive mode detected, waiting for key press...");
                            Console.WriteLine("Appuyez sur une touche pour démonter et quitter... / Press any key to unmount and exit...");
                            Console.ReadKey();
                            return UnmountImage();
                        }
                        
                        return 0; // Succès
                    }
                    else
                    {
                        string errorMsg = "Échec du montage de l'image / Failed to mount image";
                        LogMessage(errorMsg, true);
                        Console.WriteLine(errorMsg);
                        return 1;
                    }
                }
                catch (Exception ex)
                {
                    string errorMsg = $"Erreur lors du montage de l'image / Error mounting image: {ex.Message}";
                    LogMessage(errorMsg, true);
                    Console.WriteLine(errorMsg);
                    return 1;
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"Erreur inattendue / Unexpected error: {ex.Message}";
                LogMessage(errorMsg, true);
                Console.WriteLine(errorMsg);
                return 1;
            }
            
            return 0; // Fin normale du programme
        }
}
