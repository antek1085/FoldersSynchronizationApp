using Microsoft.VisualBasic.FileIO;
using SearchOption = System.IO.SearchOption;

namespace Veeam_Folders_Synchronization;

public enum FileChangeType
{
    Deleted,
    Renamed,
    Created,
    Changed
}
public class ItemsToChange()
{
    public FileChangeType ChangeType { get; set; }
    public string newName { get; set; }
    public string oldName { get; set; }
}

class Program
{
    private static string _folderDirectory;
    private static string _folderCopyDirectory;
    private static string _logFilePath;
    private static int _syncIntervalMs;
    
    private static bool _isCopyRunning = false;
    static List<ItemsToChange> _itemsToChangeList = new();

    static void Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("Use:");
            Console.WriteLine("Veeam_Folders_Synchronization.exe <Folder Directory> <Copy Folder Directory> <Log File path> <sync interval>");
            Console.WriteLine("Example:");
            Console.WriteLine(@"Veeam_Folders_Synchronization.exe C:\Test C:\TestCopy C:\log.txt 2000");
            return;
        }

       
        _folderDirectory = args[0];
        _folderCopyDirectory = args[1];
        _logFilePath = args[2];
        _syncIntervalMs = int.Parse(args[3]);

        if (_syncIntervalMs <= 0)
        {
            Console.WriteLine("Sync interval must be a positive integer");
            return;
        }
        if (_folderDirectory == _folderCopyDirectory)
        {
            Console.WriteLine("Folder Directory and Copy Directory are the same");
            return;
        }
        if (!Directory.Exists(_folderDirectory) && !Directory.Exists(_folderCopyDirectory))
        {
            Console.WriteLine("One of directory doesn't exist");
            return;
        }
        if (!File.Exists(_logFilePath))
        {
            Console.WriteLine("Log file path doesn't exist");
            return;
        }
        

        File.WriteAllText(_logFilePath, string.Empty);
        
        CopyAllFiles(_folderDirectory, _folderCopyDirectory);

        using FileSystemWatcher fileWatcher = new FileSystemWatcher(_folderDirectory);


        fileWatcher.Filter = "*.*";
        fileWatcher.IncludeSubdirectories = true;


        fileWatcher.Created += FileWatcherOnCreated;
        fileWatcher.Changed += FileWatcherOnChanged;
        fileWatcher.Deleted += FileWatcherOnDeleted;
        fileWatcher.Renamed += FileWatcherOnRenamed;
        
        fileWatcher.EnableRaisingEvents = true;
        
        using (Timer timer = new Timer(IntervalCopy, null, 0, _syncIntervalMs))
        {
            Console.WriteLine("Watching... Press Enter to exit.");
            Console.ReadLine();
        }
    }

    #region File Watcher Events

    static void FileWatcherOnRenamed(object sender, RenamedEventArgs e)
    {
        string message = Timestamp() + Environment.NewLine +
                         "Renamed File: " + e.OldName + "|| New Name: "+ e.Name + Environment.NewLine
                         + "File Path: " + e.FullPath;
        
        SaveChangesForInterval(e.FullPath,FileChangeType.Renamed,e.OldName);
        
        Console.WriteLine(message);
        LogMessageToLog(message);
    }
    
    static void FileWatcherOnDeleted(object sender, FileSystemEventArgs e)
    {
        string message;
        if (FileSystem.DirectoryExists(Path.Combine(_folderCopyDirectory, e.Name)))
        {
            SaveChangesForInterval(e.Name,FileChangeType.Deleted);
             message = Timestamp() + Environment.NewLine +
                       "Deleted Directory: " + e.Name + Environment.NewLine +
                       "Directory Path: " + e.FullPath;
        }
        else
        {
            SaveChangesForInterval(e.Name,FileChangeType.Deleted); 
             message = Timestamp() + Environment.NewLine + 
                       "Deleted File: " + e.Name + Environment.NewLine +
                       "File Path: " + e.FullPath;
        }
        
        Console.WriteLine(message);
        LogMessageToLog(message);
    }
    static void FileWatcherOnChanged(object sender, FileSystemEventArgs e)
    {
        if(e.ChangeType != WatcherChangeTypes.Changed) return;
        
        string message = Timestamp() + Environment.NewLine +
                         "Changes in File: " + e.Name + Environment.NewLine +
                         "File Path: " + e.FullPath;
        
        SaveChangesForInterval(e.Name,FileChangeType.Changed);
        
        Console.WriteLine(message);
        LogMessageToLog(message);
    }
    static void FileWatcherOnCreated(object sender, FileSystemEventArgs e)
    {
        string message = Timestamp() + Environment.NewLine +
                         "Added new File: " + e.Name + Environment.NewLine +
                         "File Path: " + e.FullPath;

        if (!e.Name.Contains("."))
        {
            Directory.CreateDirectory(Path.Combine(_folderCopyDirectory, e.Name)).Create();
            return;
        }
        
        SaveChangesForInterval(e.Name,FileChangeType.Created);
        
        Console.WriteLine(message);
        LogMessageToLog(message);
    }

    #endregion

    static void LogMessageToLog(string message)
    {
        File.AppendAllText(_logFilePath, message);
    }
    
    private static string Timestamp()
    {
        return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    #region Methods used at start

    //Initial delete files in copy Directory and copy of them
    static void CopyAllFiles(string sourceDirectory, string destinationDirectory)
    {
        foreach (string file in Directory.GetFiles(destinationDirectory))
        {
            File.Delete(file);
        }
        foreach (string dir in Directory.GetDirectories(destinationDirectory))
        {
            Directory.Delete(dir, true);
        }

        DirectoryCreation(sourceDirectory, destinationDirectory);
        
        foreach (string newPath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            File.Copy(newPath, newPath.Replace(sourceDirectory, destinationDirectory), true);
        }
        LogMessageToLog(Timestamp()+Environment.NewLine + "Copied " + sourceDirectory + " to " + destinationDirectory + ".");
    }

    
    static bool DirectoryCreation(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return true;
        }
        foreach (string subDirectory in Directory.GetDirectories(sourceDirectory))
        {
            string newDestinationDirectory = Path.Combine(destinationDirectory, Path.GetFileName(subDirectory));
            Directory.CreateDirectory(newDestinationDirectory);
            if (DirectoryCreation(subDirectory, newDestinationDirectory))
            { 
                return true;
            };
        }
        return false;
    }

    #endregion

    //Make Changes to copy folder each interval if conditions are met
    static void IntervalCopy(object? state)
    {
        if (_itemsToChangeList.Count <= 0 || _isCopyRunning) return;
        
        _isCopyRunning = true;
        
        foreach (var changes in _itemsToChangeList)
        {
            switch (changes.ChangeType)
            {
                case FileChangeType.Renamed:
                    
                    if (changes.oldName != null && changes.newName != null && Path.HasExtension(changes.newName))
                    {
                        if(!File.Exists(Path.Combine(_folderCopyDirectory,changes.newName))) break;
                        
                        File.Delete(Path.Combine(_folderCopyDirectory,changes.oldName));
                        try
                        {
                            File.Copy(Path.Combine(_folderDirectory,changes.newName),
                            Path.Combine(_folderDirectory,changes.newName)
                                .Replace(_folderDirectory,_folderCopyDirectory),overwrite:true);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Skipping locked file: {_folderDirectory}");
                        }
                    }
                    else if (!Path.HasExtension(changes.newName))
                    {
                        FileSystem.RenameDirectory(Path.Combine(_folderCopyDirectory,changes.oldName),Path.GetFileName(changes.newName));
                    }
                    break;
                
                case FileChangeType.Deleted:
                    
                    if (FileSystem.DirectoryExists(Path.Combine(_folderCopyDirectory,changes.newName)))
                    {
                        FileSystem.DeleteDirectory(Path.Combine(_folderCopyDirectory,changes.newName),DeleteDirectoryOption.DeleteAllContents);
                        break;
                    }
                    if(!File.Exists(Path.Combine(_folderCopyDirectory,changes.newName))) break;
                    
                    File.Delete(Path.Combine(_folderCopyDirectory,changes.newName));
                    break;
                
                case FileChangeType.Created:

                    File.Copy(Path.Combine(_folderDirectory,changes.newName),
                    Path.Combine(_folderDirectory,changes.newName)
                        .Replace(_folderDirectory,_folderCopyDirectory),overwrite:true);
                    break;
                
                case FileChangeType.Changed:
                    if(!File.Exists(Path.Combine(_folderCopyDirectory,changes.newName))) break;

                    try
                    {
                        File.Delete(Path.Combine(_folderCopyDirectory, changes.newName));
                        File.Copy(Path.Combine(_folderDirectory, changes.newName),
                        Path.Combine(_folderDirectory, changes.newName)
                            .Replace(_folderDirectory, _folderCopyDirectory),
                        overwrite: true);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Changes to: {_folderDirectory} folder");
                    }
                    break;
                
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        _isCopyRunning = false;
        _itemsToChangeList.Clear();
    }

    static void SaveChangesForInterval(string newName, FileChangeType changeType, string? oldName = null)
    {
        var var = new ItemsToChange();
        var.ChangeType = changeType;
        var.newName = newName;
        var.oldName = oldName;
        
        _itemsToChangeList.Add(var);
    }
}