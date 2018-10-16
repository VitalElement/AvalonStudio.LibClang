/////////////////////////////////////////////////////////////////////
// ADDINS
/////////////////////////////////////////////////////////////////////

#addin "nuget:?package=Polly&version=5.0.6"
#addin "nuget:?package=SharpZipLib&version=1.0.0"
#addin "nuget:?package=Cake.Compression&version=0.2.1"

//////////////////////////////////////////////////////////////////////
// TOOLS
//////////////////////////////////////////////////////////////////////

#tool "nuget:?package=NuGet.CommandLine&version=4.3.0"

///////////////////////////////////////////////////////////////////////////////
// USINGS
///////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.IO;
using Polly;

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var platform = Argument("platform", "AnyCPU");
var configuration = Argument("configuration", "Release");

///////////////////////////////////////////////////////////////////////////////
// CONFIGURATION
///////////////////////////////////////////////////////////////////////////////

var MainRepo = "VitalElement/AvalonStudio.Toolchains.ClangToolchain";
var MasterBranch = "master";
var ReleasePlatform = "Any CPU";
var ReleaseConfiguration = "Release";

///////////////////////////////////////////////////////////////////////////////
// PARAMETERS
///////////////////////////////////////////////////////////////////////////////

var isLocalBuild = BuildSystem.IsLocalBuild;
var isRunningOnUnix = IsRunningOnUnix();
var isRunningOnWindows = IsRunningOnWindows();
var isRunningOnAppVeyor = BuildSystem.AppVeyor.IsRunningOnAppVeyor;
var isPullRequest = BuildSystem.AppVeyor.Environment.PullRequest.IsPullRequest;
var isMainRepo = StringComparer.OrdinalIgnoreCase.Equals(MainRepo, BuildSystem.AppVeyor.Environment.Repository.Name);
var isMasterBranch = StringComparer.OrdinalIgnoreCase.Equals(MasterBranch, BuildSystem.AppVeyor.Environment.Repository.Branch);
var isTagged = BuildSystem.AppVeyor.Environment.Repository.Tag.IsTag 
               && !string.IsNullOrWhiteSpace(BuildSystem.AppVeyor.Environment.Repository.Tag.Name);

///////////////////////////////////////////////////////////////////////////////
// VERSION
///////////////////////////////////////////////////////////////////////////////

var version = "7.0.0";

if (isRunningOnAppVeyor)
{
    if (isTagged)
    {
        // Use Tag Name as version
        version = BuildSystem.AppVeyor.Environment.Repository.Tag.Name;
    }
    else
    {
        // Use AssemblyVersion with Build as version
        version += "-build" + EnvironmentVariable("APPVEYOR_BUILD_NUMBER") + "-alpha";
    }
}

///////////////////////////////////////////////////////////////////////////////
// DIRECTORIES
///////////////////////////////////////////////////////////////////////////////

var artifactsDir = (DirectoryPath)Directory("./artifacts");
var zipRootDir = artifactsDir.Combine("zip");
var nugetRoot = artifactsDir.Combine("nuget");
var fileZipSuffix = ".zip";

private bool MoveFolderContents(string SourcePath, string DestinationPath)
{
   SourcePath = SourcePath.EndsWith(@"/") ? SourcePath : SourcePath + @"/";
   DestinationPath = DestinationPath.EndsWith(@"/") ? DestinationPath : DestinationPath + @"/";
 
   try
   {
      if (System.IO.Directory.Exists(SourcePath))
      {
         if (System.IO.Directory.Exists(DestinationPath) == false)
         {
            System.IO.Directory.CreateDirectory(DestinationPath);
         }
 
         foreach (string files in System.IO.Directory.GetFiles(SourcePath))
         {
            FileInfo fileInfo = new FileInfo(files);
            fileInfo.MoveTo(string.Format(@"{0}/{1}", DestinationPath, fileInfo.Name));
         }
 
         foreach (string drs in System.IO.Directory.GetDirectories(SourcePath))
         {
            System.IO.DirectoryInfo directoryInfo = new DirectoryInfo(drs);
            if (MoveFolderContents(drs, DestinationPath + directoryInfo.Name) == false)
            {
               return false;
            }
         }
      }
      return true;
   }
   catch (Exception ex)
   {
      return false;
   }
}

public class ArchiveDownloadInfo
{
    public string URL {get;set;}
    public string Name {get;set;}
    public FilePath DestinationFile {get; set;}
    public string Format {get; set;}
    public Action<DirectoryPath, ArchiveDownloadInfo> PostExtract {get; set;}
}

public class ToolchainDownloadInfo
{
    private DirectoryPath _artifactsDir;

    public ToolchainDownloadInfo(DirectoryPath artifactsDir)
    {
        _artifactsDir = artifactsDir;
        Downloads = new List<ArchiveDownloadInfo>();
    }

    public DirectoryPath BaseDir {get { return _artifactsDir.Combine(RID); } }

    public DirectoryPath ZipDir {get { return _artifactsDir.Combine("zip").Combine(RID); } }

    public string RID {get; set;}
    public List<ArchiveDownloadInfo> Downloads {get; set;}
    
}

var toolchainDownloads = new List<ToolchainDownloadInfo> 
{ 
    /*new ToolchainDownloadInfo (artifactsDir)
    { 
        RID = "win-x64", 
        Downloads = new List<ArchiveDownloadInfo>()
        { 
            new ArchiveDownloadInfo()
            { 
                Format = "exe", 
                DestinationFile = "clang.exe", 
                URL =  "http://releases.llvm.org/7.0.0/LLVM-7.0.0-win64.exe",
                Name = "LLVM-7.0.0-win64.exe",
                PostExtract = (curDir, info) =>{

                    if(DirectoryExists(curDir.Combine("$PLUGINSDIR")))
                    {
                        DeleteDirectory(curDir.Combine("$PLUGINSDIR"), true);
                    }
                }
            }
        }
    },
    new ToolchainDownloadInfo (artifactsDir)
    { 
        RID = "ubuntu-x64", 
        Downloads = new List<ArchiveDownloadInfo>()
        { 
            new ArchiveDownloadInfo()
            { 
                Format = "tar.xz", 
                DestinationFile = "clang.tar.xz", 
                URL =  "http://releases.llvm.org/7.0.0/clang+llvm-7.0.0-x86_64-linux-gnu-ubuntu-14.04.tar.xz",
                Name = "clang+llvm-7.0.0-x86_64-linux-gnu-ubuntu-14.04",
                PostExtract = (curDir, info) =>{

                    var tarFile = curDir.CombineWithFilePath("clang.tar");
                    
                    StartProcess("7z", new ProcessSettings{ Arguments = string.Format("x {0} -o{1}", tarFile.ToString(), curDir.ToString()) });

                    MoveFolderContents(curDir.Combine(info.Name).ToString(), curDir.ToString());

                    DeleteFile(tarFile);
                    //DeleteDirectory(curDir.Combine(info.Name), true);
                }
            },
            new ArchiveDownloadInfo()
            {
                Format = "tar.bz2",
                DestinationFile = "gcc.tar.bz2",
                URL = "https://developer.arm.com/-/media/Files/downloads/gnu-rm/7-2017q4/gcc-arm-none-eabi-7-2017-q4-major-linux.tar.bz2?revision=375265d4-e9b5-41c8-bf23-56cbe927e156?product=GNU Arm Embedded Toolchain,64-bit,,Linux,7-2017-q4-major",
                Name= "gcc-arm-none-eabi-7-2017-q4-major",
                PostExtract = (curDir, info)=>
                {
                    var tarFile = curDir.CombineWithFilePath("gcc.tar");
                    Information(tarFile);
                    var args = string.Format("x {0} -o{1}", tarFile.ToString(), curDir.ToString());
                    Information(args);
                    StartProcess("7z", new ProcessSettings{ Arguments = args, WorkingDirectory = curDir } );

                    Information(curDir.ToString());
                    Information(curDir.Combine(info.Name).ToString());

                    MoveFolderContents(curDir.Combine(info.Name).ToString(), curDir.ToString());

                    DeleteFile(tarFile);
                    //DeleteDirectory(curDir.Combine(info.Name), true);
                }
            }
        }
    },*/
    new ToolchainDownloadInfo (artifactsDir)
    { 
        RID = "osx-x64", 
        Downloads = new List<ArchiveDownloadInfo>()
        { 
            new ArchiveDownloadInfo()
            { 
                Format = "tar.xz", 
                DestinationFile = "clang.tar.xz", 
                URL =  "http://releases.llvm.org/7.0.0/clang+llvm-7.0.0-x86_64-apple-darwin.tar.xz",
                Name = "clang+llvm-7.0.0-x86_64-apple-darwin",
                PostExtract = (curDir, info) =>{
                    var tarFile = curDir.CombineWithFilePath("clang.tar");
                    StartProcess("7z", new ProcessSettings{ Arguments = string.Format("x {0} -o{1}", tarFile, curDir) });
                    DeleteFile(tarFile);

                    MoveFolderContents(curDir.Combine(info.Name).ToString(), curDir.ToString());

                    DeleteDirectory(curDir.Combine(info.Name), true);
                }
            },
            /*new ArchiveDownloadInfo()
            {
                Format = "tar.bz2",
                DestinationFile = "gcc.bz2",
                URL = "https://developer.arm.com/-/media/Files/downloads/gnu-rm/7-2017q4/gcc-arm-none-eabi-7-2017-q4-major-mac.tar.bz2?revision=7f453378-b2c3-4c0d-8eab-e7d5db8ea32e?product=GNU Arm Embedded Toolchain,64-bit,,Mac OS X,7-2017-q4-major",
                Name= "gcc-arm-none-eabi-7-2017-q4-major",
                PostExtract = (curDir, info)=>
                {
                    StartProcess("7z", new ProcessSettings{ Arguments = string.Format("x {0} -o{1}", curDir.CombineWithFilePath("gcc").ToString(), curDir.ToString()) });

                    MoveFolderContents(curDir.Combine(info.Name).ToString(), curDir.ToString());

                    DeleteFile(curDir.CombineWithFilePath("gcc"));
                    DeleteDirectory(curDir.Combine(info.Name), true);
                }
            }*/
        }
    }
};

///////////////////////////////////////////////////////////////////////////////
// NUGET NUSPECS
///////////////////////////////////////////////////////////////////////////////
public NuGetPackSettings GetPackSettings(string rid)
{
    var nuspecNuGetBehaviors = new NuGetPackSettings()
    {
        Id = "AvalonStudio.Libclang.",
        Version = version,
        Authors = new [] { "VitalElement" },
        Owners = new [] { "Dan Walmsley" },
        LicenseUrl = new Uri("http://opensource.org/licenses/MIT"),
        ProjectUrl = new Uri("https://github.com/VitalElement/"),
        RequireLicenseAcceptance = false,
        Symbols = false,
        NoPackageAnalysis = true,
        Description = "Libclang Binaries for AvalonStudio",
        Copyright = "Copyright 2018",
        Tags = new [] { "clang", "AvalonStudio", "Toolchain" },
        Files = new []
        {
            new NuSpecContent { Source = "**", Target = "runtimes/osx-x64/native/" },
        },
        BasePath = Directory("artifacts/" + rid + "/"),
        OutputDirectory = nugetRoot
    };

    return nuspecNuGetBehaviors;
}

///////////////////////////////////////////////////////////////////////////////
// INFORMATION
///////////////////////////////////////////////////////////////////////////////


///////////////////////////////////////////////////////////////////////////////
// TASKS
/////////////////////////////////////////////////////////////////////////////// 

Task("Clean")
.Does(()=>{    
    foreach(var tc in toolchainDownloads)
    {
        /*CleanDirectory(tc.BaseDir);   
        CleanDirectory(tc.ZipDir);*/
    }

    CleanDirectory(nugetRoot);
});

Task("Download-Toolchains")
.Does(()=>{
    foreach(var tc in toolchainDownloads)
    {
        foreach(var downloadInfo in tc.Downloads)
        {
            var fileName = tc.ZipDir.CombineWithFilePath(downloadInfo.DestinationFile);

            if(!FileExists(fileName))
            {
                DownloadFile(downloadInfo.URL, fileName);
            }
        }
    }
});

Task("Extract-Toolchains")
.Does(()=>{
    foreach(var tc in toolchainDownloads)
    {
        foreach(var downloadInfo in tc.Downloads)
        {
            var fileName = tc.ZipDir.MakeAbsolute(Context.Environment).CombineWithFilePath(downloadInfo.DestinationFile);
            var dest = tc.BaseDir.MakeAbsolute(Context.Environment);

            switch (downloadInfo.Format)
            {
                case "zip":
                ZipUncompress(fileName, dest);
                break;

                default:
                case "tar.bz2":
                case "tar.xz":                
                ProcessSettings settings = new ProcessSettings{ Arguments = string.Format("x {0} -aoa -o{1}", fileName, dest) };

                Information("7z " + settings.Arguments.Render());

                StartProcess("7z", settings);
                break;
            }        

            if(downloadInfo.PostExtract != null)
            {
                downloadInfo.PostExtract(dest, downloadInfo);
            }
        }
    }
});

Task("Generate-NuGetPackages")
.Does(()=>{
    foreach(var tc in toolchainDownloads)
    {
        NuGetPack(GetPackSettings(tc.RID));
    }
});

Task("Publish-AppVeyorNuget")
    .IsDependentOn("Generate-NuGetPackages")        
    .WithCriteria(() => isMainRepo)
    .WithCriteria(() => isMasterBranch)    
    .Does(() =>
{
    var apiKey = EnvironmentVariable("APPVEYOR_NUGET_API_KEY");
    if(string.IsNullOrEmpty(apiKey)) 
    {
        throw new InvalidOperationException("Could not resolve MyGet API key.");
    }

    var apiUrl = EnvironmentVariable("APPVEYOR_ACCOUNT_FEED_URL");
    if(string.IsNullOrEmpty(apiUrl)) 
    {
        throw new InvalidOperationException("Could not resolve MyGet API url.");
    }

    foreach(var tc in toolchainDownloads)
    {
        var nuspec = GetPackSettings(tc.RID);
        var settings  = nuspec.OutputDirectory.CombineWithFilePath(string.Concat(nuspec.Id, ".", nuspec.Version, ".nupkg"));

        NuGetPush(settings, new NuGetPushSettings
        {
            Source = apiUrl,
            ApiKey = apiKey,
            Timeout = TimeSpan.FromMinutes(45)
        });
    }
});

Task("Default")    
    .IsDependentOn("Clean")
    //.IsDependentOn("Download-Toolchains")
    //.IsDependentOn("Extract-Toolchains")
    .IsDependentOn("Generate-NuGetPackages");
    //.IsDependentOn("Publish-AppVeyorNuget");
RunTarget(target);