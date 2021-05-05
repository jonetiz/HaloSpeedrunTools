using System;
using System.Collections.Generic;
using System.Linq; 
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Forms;
using System.ComponentModel; //for canceleventargs
using System.Security.Principal; //used for checking if we have admin privs
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization; //used for json stuff
using System.Text.RegularExpressions; //used for json stuff
using System.IO; //for writing/reading files
using System.Runtime.InteropServices; //for DllImport
using System.Windows.Threading; //used for timer (yeah threading is a thing ouch)
using System.Diagnostics; //required for handling processhandles and whatnot
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;

namespace HaloSpeedrunTools
{

    public partial class MainWindow : Window
    {

        //functions for reading from process memory
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);


        //used for writing process memory, which we don't need to do
        //[DllImport("kernel32.dll", SetLastError = true)]
        //public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesWritten);

        const int PROCESS_WM_READ = 0x0010;             //flags we need to supply when opening the process to be able to read memory
                                                        //const int PROCESS_ALL_ACCESS = 0x1F0FFF;      //we'd use this flag instead if we wanted to be able to write memory too


        //here is where we keep config variables, stuff like what checkboxes the user has set. we save these to a file when stub program closes, load them when we start it up. 
        private class ConfigVars
        {
            //can set default values but that's optional

            public string wsPassword;

        }


        //here's where we can store global variables 
        private static class GlobalVars
        {
            public static readonly string LocalDir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            public static readonly string ConfigPath = LocalDir + @"\config.json";
            public static readonly string OffsetsPath = LocalDir + @"\offsets\";

            public static ConfigVars SavedConfig;
            public static Offsets LoadedOffsets;

            //info we'll get when we attach to the mcc process
            public static Int32? ProcessID;
            public static IntPtr GlobalProcessHandle;
            public static IntPtr BaseAddress;
            public static string AttachedGame = "No"; //No, Mn (menu), HR, H1, H2, H3, OD (ODST), H4
            public static string AttachedLevel;

            //control stuff
            public static bool WinStoreFlag; //false == steam, true == winstore. used for knowing which offsets to use
            public static bool VersionCheckedFlag = false;
            public static string MCCversion;
            public static bool OffsetsAcquired = false;
            public static bool GotBaseAddress = false;
            public static int mainloopcounter = 0;


            //our actually useful ingame values we'll keep track of
            public static int CurrentTickCount;



        }

        //here's the game memory offsets (for stuff like tickcounter etc) that we'll later load from a json file downloaded from github. 
        private class Offsets
        {
            //general
            public int[][] gameindicator;
            //0 is ce
            //1 is h2
            //2 is h3
            //3 is h4
            //4 is ?? h2a multiplayer maybe?
            //5 is ODST
            //6 is reach

            public int[][] menuindicator;
            //07 == in main menu 
            //!= 07 is ingame
            public int[][] stateindicator;
            //255 == ingame and unpause
            //129 == ingame and paused
            //44 == loading screen
            //57 == on Post-Game Carnage Report

            public int[][] H1_LevelName;
            public int[][] H2_LevelName;
            public int[][] H3_LevelName;
            public int[][] OD_LevelName;
            public int[][] H4_LevelName;
            public int[][] HR_LevelName;

            public int[][] H1_CheckString;
            public int[][] H1_TickCounter;

        }



        private readonly string[] RequiredFiles =
        {
            @"config.json"
        };

        private readonly string[] RequiredFolders =
        {
            @"offsets",
        };


        public static bool IsElevated
        {
            get
            {
                return WindowsIdentity.GetCurrent().Owner
                  .IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid);
            }
        }


        protected OBSWebsocket obs;

        public MainWindow()
        {
            //init ui
            InitializeComponent();

            if (IsElevated == false)
            {
                //popup error message that we need admin privledges, then close the application
                MessageBoxResult messageBoxResult = System.Windows.MessageBox.Show(" this thingy needs admin privileges to operate, the application will now close. \n To run as admin, right click the exe and 'Run As Administrator' \n \n If you're (rightfully) cautious of giving software admin privs, \n feel free to inspect/build the source from over at \n uh wherever this is hosted ", "Error", System.Windows.MessageBoxButton.OK);
                System.Windows.Application.Current.Shutdown();
            }
            obs = new OBSWebsocket();
            obs.Connected += onConnect;
            obs.Disconnected += onDisconnect;
            obs.OBSExit += onDisconnect;
        }

        //this is run once the ui finishes loading
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

            Console.WriteLine("Window loaded!");



            // Validate that required folders exist
            foreach (var folder in RequiredFolders)
            {
                var folderPath = $@"{GlobalVars.LocalDir}\{folder}";
                try
                {
                    Directory.CreateDirectory(folderPath);
                }
                catch (Exception exp)
                {
                    Console.WriteLine($@"Exception creating folder {folderPath}: {exp}");
                }
            }


            // Validate that required files exist
            foreach (var file in RequiredFiles)
            {
                var filePath = $@"{GlobalVars.LocalDir}\{file}";
                try
                {
                    if (!File.Exists(filePath))
                    {
                        File.CreateText(filePath).Close();
                    }
                }
                catch (Exception exp)
                {
                    Console.WriteLine($@"Exception creating file {filePath}: {exp}");
                }
            }


            // Set up Config
            using (StreamReader r = new StreamReader(GlobalVars.ConfigPath))
            {
                string json = r.ReadToEnd();
                GlobalVars.SavedConfig = JsonConvert.DeserializeObject<ConfigVars>(json);
            }

            // Verify config was loaded, otherwise create a new one
            if (GlobalVars.SavedConfig == null)
            {
                GlobalVars.SavedConfig = new ConfigVars();
            }




            //now let's set internal variables corrosponding to the config we loaded

            wsPasswordField.Password = GlobalVars.SavedConfig.wsPassword;






            //establish our timer that calls mainloop every x seconds or whatever
            //if we want to use a very short period on this (eg 30ms), it's possible to run into issues like our ui locking up. DistpacherTimer runs on the same thread as wpf ui stuff, so our mainloop needs to finish processing relatively quickly so we can handle mouse inputs and etc.
            //if we wanted to avoid this we could use System.Threading.Timer or System.Timers.Timer, but then we'd have to worry about multithreading issues which is a PITA I can't be bothered about
            DispatcherTimer dtClockTime = new DispatcherTimer();
            dtClockTime.Interval = new TimeSpan(0, 0, 0, 0, 30); //in days, Hours, Minutes, Second, milliseconds.
            dtClockTime.Tick += mainloop;
            dtClockTime.Start();



        }



        //called when user closes the window. we wanna save our config stuff here.
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            //gotta set the savedconfig vars to what they should be, then we'll save them to file with WriteConfig.

            GlobalVars.SavedConfig.wsPassword = wsPasswordField.Password;
            WriteConfig();
        }



        public static void WriteConfig()
        {
            try
            {
                string json = JsonConvert.SerializeObject(GlobalVars.SavedConfig, Formatting.Indented);
                File.WriteAllText(GlobalVars.ConfigPath, json);
            }
            catch (Exception e)
            {
                Console.WriteLine("problem writing config" + e);
            }
        }

        //converts a hexadecimal number string to a decimal number string
        public static string Hex2Dec(string m)
        {
            return int.Parse(m, System.Globalization.NumberStyles.HexNumber).ToString();
        }


        //resolves multilevel pointers, for use in ReadProcessMemory
        public static IntPtr FindPointerAddy(IntPtr hProc, IntPtr ptr, int[] offsets)
        {
            var buffer = new byte[IntPtr.Size];

            ptr = ptr + offsets[0];
            if (offsets.Length == 1)
            {
                return ptr;
            }

            offsets = offsets.Skip(1).ToArray();

            foreach (int i in offsets)
            {
                ReadProcessMemory(hProc, ptr, buffer, buffer.Length, out int read);

                ptr = (IntPtr.Size == 4)
                ? IntPtr.Add(new IntPtr(BitConverter.ToInt32(buffer, 0)), i)
                : ptr = IntPtr.Add(new IntPtr(BitConverter.ToInt64(buffer, 0)), i);
            }
            return ptr;
        }

        private void mainloop(object sender, EventArgs e)
        {

            //welcome to the overcomplicated mainloop, where we attach to the process, get our json file of offsets, and do anything else we want to repeatedly do on a timer, like read the games memory!

            //if we have a non-null processhandle, see if it is still valid

            IntPtr processHandle;
            Process myProcess = null;


            //if we've previously attached to mcc, check if the processID is still valid (eg process is still running at that id)
            try
            {
                myProcess = Process.GetProcessById(GlobalVars.ProcessID.GetValueOrDefault()); //this will throw an exception if no process exists at that id
                if (myProcess.Id == 0)
                {
                    myProcess = null;
                }
            }
            catch
            {
                myProcess = null;
               // Console.WriteLine("mcc process didn't exist at previous ID");
            }


            if (myProcess == null)
            {
                //we need to check the list of processes on the system and see if MCC is on it. then get the processhandle nad open it etc

                //there's 3 different processnames we have to check

                bool AttachProcess(string processname)
                {
                    try
                    {
                        myProcess = Process.GetProcessesByName(processname)[0];
                        processHandle = OpenProcess(PROCESS_WM_READ, false, myProcess.Id);
                        GlobalVars.ProcessID = myProcess.Id;
                        GlobalVars.GlobalProcessHandle = processHandle;
                        //Console.WriteLine("MCC found with ID " + (Convert.ToString(myProcess.Id, 16)).ToUpper());
                        GlobalVars.WinStoreFlag = processname.Contains("Store"); //as in Winstore
                        return true;
                    }
                    catch
                    {
                       // Console.WriteLine("Didn't find mcc with name: " + processname);
                        return false;
                    }
                }


                if (!AttachProcess("MCC-Win64-Shipping"))
                {
                    if (!AttachProcess("MCC-Win64-Shipping-WinStore"))
                    {
                        if (!AttachProcess("MCCWinStore-Win64-Shipping"))
                        {
                            //well shit
                            //Console.WriteLine("MCC process not open.");
                            return; //halt mainloop execution, nothing more to be done without an open mcc process.
                        }
                    }
                }

            }


            //alright, now we should be attached to the mcc process.

            //what mcc version are we on? only need to check this once, so we have a flag for keeping track of this
            if (!GlobalVars.VersionCheckedFlag)
            {
                try
                {
                    GlobalVars.MCCversion = myProcess.MainModule.FileVersionInfo.ProductVersion.ToString();
                    Console.WriteLine("MCC version: " + GlobalVars.MCCversion + ", WinFlag: " + GlobalVars.WinStoreFlag.ToString());
                    GlobalVars.VersionCheckedFlag = true;

                }

                catch
                {
                    Console.WriteLine("MCC - failed to find version info");
                    return; //somethings gone horribly wrong, we'll try again on the next mainloop
                }
            }


            //now, have we loaded the offsets from the json file yet? if not, we need to get them (either from online or locally)
            //we only need to run this once, so we have OffsetsAcquired to ensure that. if we fail to acquire the offsets we'll popup a dialogue then force close the program (nothing we can do without offsets).
            if (!GlobalVars.OffsetsAcquired)
            {

                //first let's check if we have a local json file corresponding to the correct version.
                string localfile = GlobalVars.OffsetsPath + GlobalVars.MCCversion + ".json";
                bool havelocalfile = File.Exists(localfile);
                bool needtodownloadonlinefile = !havelocalfile;
                DateTime? lastCommitDate = null;
                //then check if we can see the same json file online on the github page. if so, we'll compare the file dates and use the latest. if not, we'll just use the local file. 


                void bailandclose()
                {
                    MessageBoxResult messageBoxResult = System.Windows.MessageBox.Show("The program failed to acquire offsets for the attached MCC version. \n This probably means MCC got a patch, and you'll have to wait for this programs author \n to update this program to actually work. \n Program will close when you click OK. ", "Error", System.Windows.MessageBoxButton.OK);
                    System.Windows.Application.Current.Shutdown();
                    return;
                }

                //first we can check the github api to get the datetime of the latest commit
                try
                {
                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("User-Agent",
                            "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.2; WOW64; Trident/6.0)");

                        using (var response = client.GetAsync("https://api.github.com/repos/Burnt-o/StubStuff/commits?path=StubStuff/offsets/" + GlobalVars.MCCversion + ".json").Result)
                        {
                            var json = response.Content.ReadAsStringAsync().Result;

                            dynamic commits = JArray.Parse(json);
                            string lastCommit = commits[0].commit.author.date;
                            Console.WriteLine("last commit: " + lastCommit);
                            //gotta parse the last commit string to an actual datetime thing. github uses ISO 8601 datetime format.
                            lastCommitDate = DateTime.Parse(lastCommit, null, System.Globalization.DateTimeStyles.RoundtripKind);



                        }
                    }
                }
                catch
                {
                    Console.WriteLine("Failed to check the commit history. probably means the online file doesn't actually exist, but we'll try again in a sec");
                }


                if (havelocalfile && lastCommitDate != null)
                {
                    //let's check which is more recent

                    //get local file creation date
                    FileInfo fi = new FileInfo(localfile);
                    DateTime creationTime = fi.CreationTime;

                    //compare
                    if (DateTime.Compare(creationTime, (DateTime)lastCommitDate) < 0)
                    //compare returns a signed int, that's less than zero if the first datetime is EARLIER than the second datetime
                    //in this case that means that the online file is *more recent* than the local file, meaning we want it (might have important bugfixes)
                    {
                        needtodownloadonlinefile = true;
                    }

                }

                if (needtodownloadonlinefile)
                {
                    //here's the fun
                    try
                    {
                        String url = "https://raw.githubusercontent.com/Burnt-o/StubStuff/master/StubStuff/offsets/" + GlobalVars.MCCversion + ".json";
                        System.Net.WebClient client = new System.Net.WebClient();
                        String json = client.DownloadString(url);
                        System.IO.File.WriteAllText("offsets\\" + GlobalVars.MCCversion + ".json", json);
                        Console.WriteLine("sucessfully downloaded online json file!");
                    }
                    catch
                    {
                        Console.WriteLine("failed to download online json file!");
                        if (!havelocalfile)
                        {
                            //no local file, can't get online file. therefore no offsets, we're fucked. 
                            //we'll show the user a message then force close the program.
                            bailandclose();
                            return;
                        }

                    }


                }

                //extra safety check
                if (!File.Exists(localfile))
                {
                    bailandclose();
                    return;
                }

                //okay, let's load the offsets from the local file into our program here. ie parsing json

                using (StreamReader r = new StreamReader(localfile))
                {
                    string json2 = r.ReadToEnd();
                    r.Close();
                    ITraceWriter traceWriter = new MemoryTraceWriter();
                    try
                    {

                        var s = json2;
                        var res = Regex.Replace(s, @"(?i)\b0x([a-f0-9]+)\b", m => Hex2Dec(m.Groups[1].Value));
                        Console.WriteLine(res);
                        GlobalVars.LoadedOffsets = JsonConvert.DeserializeObject<Offsets>(res, new JsonSerializerSettings { TraceWriter = traceWriter, });
                    }
                    catch
                    {
                        Console.WriteLine("something went horrifically wrong trying to load  local json file");
                        Console.WriteLine(traceWriter.ToString());
                        if (File.Exists(localfile))
                        {
                            try
                            {
                                File.Delete(localfile);
                                Console.WriteLine("deleted local json file since it was bunk");
                            }
                            catch
                            {
                                Console.WriteLine("failed to delete bunk  local json file");
                            }
                        }
                        bailandclose();
                        return;

                    }

                }


                //let's double check we got the offsets loaded correctly, and set our loadedoffsets flag!

                if (GlobalVars.LoadedOffsets.gameindicator[0][0] > 0)
                {
                    GlobalVars.OffsetsAcquired = true; //we're done here
                }
                else
                {
                    bailandclose();
                    return;
                }


            }


            //alright next up, we need to get the base address of the mcc process. 
            //OKAY we need to get the base address of mcc.exe
            //again we only need to check this once

            if (!GlobalVars.GotBaseAddress)
            {
                ProcessModule myProcessModule;
                ProcessModuleCollection myProcessModuleCollection = myProcess.Modules;

                for (int i = 0; i < myProcessModuleCollection.Count; i++)
                {
                    myProcessModule = myProcessModuleCollection[i];

                    switch (myProcessModule.ModuleName)
                    {
                        case "MCC-Win64-Shipping.exe":
                            GlobalVars.BaseAddress = myProcessModule.BaseAddress;
                            break;

                        case "MCC-Win64-Shipping-WinStore.exe":
                            GlobalVars.BaseAddress = myProcessModule.BaseAddress;
                            break;

                        case "MCCWinStore-Win64-Shipping.exe":
                            GlobalVars.BaseAddress = myProcessModule.BaseAddress;
                            break;

                        default:
                            break;

                    }
                }

                if (GlobalVars.BaseAddress == null)
                {
                    Console.WriteLine("How did execution even get to here? Something went really wrong");
                    return;
                }
                else
                {
                    //success!
                    Console.WriteLine("Sucessfully got MCC base address: " + GlobalVars.BaseAddress);
                    GlobalVars.GotBaseAddress = true;
                }

            }

            //cool, we're basically done. we can start reading MCC's memory. 


            //But.. I'm gonna add some logic for helping to keep track of whether MCC is in a game (& which game) vs in a menu, etc. 
            //now we don't really need to bother checking this super frequently, so we'll have a counter, and only run this every 100th iteration of mainloop. 
            if (GlobalVars.mainloopcounter < 100)
            {
                GlobalVars.mainloopcounter += 1;
            }
            else
            {
                GlobalVars.mainloopcounter = 0;

                //now let's do our logic!
                //we'll read what I call the "gameindicator", a simple byte value. 0 is for ce, 6 is for reach etc. 
                //but this gameindicator doesn't update when we quit to the menu, only when we load into a game. on booting MCC it defaults to 0.
                //so we'll also need to read the "menuindicator" to check if we're in a menu or not and update our value there.

                //first the gameindicator logic
                byte[] buffer = new byte[1];
                if (ReadProcessMemory(GlobalVars.GlobalProcessHandle, FindPointerAddy(GlobalVars.GlobalProcessHandle, GlobalVars.BaseAddress, GlobalVars.LoadedOffsets.gameindicator[Convert.ToInt32(GlobalVars.WinStoreFlag)]), buffer, buffer.Length, out int bytesRead))
                //bit overwhelming isn't it? I'll explain more how ReadProcessMemory works in our GetMCCValues later on, for now,
                //all you need to know that it returns true if it succeeds, and the value we care about will be in the buffer variable.
                {
                    switch (buffer[0])
                    {
                        case 0:
                            GlobalVars.AttachedGame = "H1";
                            break;
                        case 1:
                            GlobalVars.AttachedGame = "H2";
                            break;
                        case 2:
                            GlobalVars.AttachedGame = "H3";
                            break;
                        case 3:
                            GlobalVars.AttachedGame = "H4";
                            break;
                        case 4:
                            GlobalVars.AttachedGame = "Mn"; //nfi - h2a mp maybe?
                            break;
                        case 5:
                            GlobalVars.AttachedGame = "OD"; //as in ODST
                            break;
                        case 6:
                            GlobalVars.AttachedGame = "HR";
                            break;
                        default:
                            GlobalVars.AttachedGame = "Mn"; //as in menu
                            break;

                    }
                    Console.WriteLine("gameindicator is: " + buffer[0]);
                }
                else //means reading the gameindicator failed
                {
                    Console.WriteLine("failed to read game indicator");
                    Console.WriteLine("winflag?: " + GlobalVars.WinStoreFlag);
                    Console.WriteLine("a " + GlobalVars.BaseAddress);
                    Console.WriteLine("b " + GlobalVars.GlobalProcessHandle);
                    return;
                }

                //then the menuindicator logic
                if (ReadProcessMemory(GlobalVars.GlobalProcessHandle, FindPointerAddy(GlobalVars.GlobalProcessHandle, GlobalVars.BaseAddress, GlobalVars.LoadedOffsets.menuindicator[Convert.ToInt32(GlobalVars.WinStoreFlag)]), buffer, buffer.Length, out int bytesRead2))
                {
                    Console.WriteLine("menu indicator is: " + buffer[0]);
                    if (buffer[0] != 0x07)
                        GlobalVars.AttachedGame = "Mn";

                }
                else
                {
                    Console.WriteLine("failed to read menu indicator");
                    return;
                }



                //neat, that's AttachedGame done. Now let's get the name of the currently loaded level ("AttachedLevel")

                buffer = new byte[32];
                string holdstring;
                //we'll need to read the levelname from a different offset for each game. so we switch on AttachedHame
                switch (GlobalVars.AttachedGame)
                {


                    default:
                    case "Mn":
                    case "No":
                        GlobalVars.AttachedLevel = null;
                        break;

                    case "H1":
                        if (ReadProcessMemory(GlobalVars.GlobalProcessHandle, FindPointerAddy(GlobalVars.GlobalProcessHandle, GlobalVars.BaseAddress, GlobalVars.LoadedOffsets.H1_LevelName[Convert.ToInt32(GlobalVars.WinStoreFlag)]), buffer, buffer.Length, out bytesRead))
                        {
                            holdstring = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                            holdstring = holdstring.Substring(holdstring.LastIndexOf("\\") + 1);
                            holdstring = holdstring.Substring(holdstring.LastIndexOf("\\") + 1);
                            char[] exceptions = new char[] { '_' };
                            holdstring = String.Concat(holdstring.Where(ch => Char.IsLetterOrDigit(ch) || exceptions?.Contains(ch) == true));
                            Console.WriteLine("read h1 level: " + holdstring);
                            GlobalVars.AttachedLevel = holdstring;
                        }
                        else
                        {
                            Console.WriteLine("failed to read h1 level");
                            GlobalVars.AttachedLevel = null;
                        }
                        break;

                    case "H2":

                        if (ReadProcessMemory(GlobalVars.GlobalProcessHandle, FindPointerAddy(GlobalVars.GlobalProcessHandle, GlobalVars.BaseAddress, GlobalVars.LoadedOffsets.H2_LevelName[Convert.ToInt32(GlobalVars.WinStoreFlag)]), buffer, buffer.Length, out bytesRead))
                        {
                            holdstring = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                            holdstring = holdstring.Substring(holdstring.LastIndexOf("\\") + 1);
                            holdstring = holdstring.Substring(holdstring.LastIndexOf("\\") + 1);
                            char[] exceptions = new char[] { '_' };
                            holdstring = String.Concat(holdstring.Where(ch => Char.IsLetterOrDigit(ch) || exceptions?.Contains(ch) == true));
                            Console.WriteLine("read h2 level: " + holdstring);
                            GlobalVars.AttachedLevel = holdstring;
                        }
                        else
                        {
                            Console.WriteLine("failed to read h2 level");
                            GlobalVars.AttachedLevel = null;
                        }
                        break;

                    case "H3":

                        if (ReadProcessMemory(GlobalVars.GlobalProcessHandle, FindPointerAddy(GlobalVars.GlobalProcessHandle, GlobalVars.BaseAddress, GlobalVars.LoadedOffsets.H3_LevelName[Convert.ToInt32(GlobalVars.WinStoreFlag)]), buffer, buffer.Length, out bytesRead))
                        {
                            holdstring = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                            holdstring = holdstring.Substring(holdstring.LastIndexOf("\\") + 1);
                            holdstring = holdstring.Substring(holdstring.LastIndexOf("\\") + 1);
                            char[] exceptions = new char[] { '_' };
                            holdstring = String.Concat(holdstring.Where(ch => Char.IsLetterOrDigit(ch) || exceptions?.Contains(ch) == true));
                            Console.WriteLine("read h3 level: " + holdstring);
                            GlobalVars.AttachedLevel = holdstring;
                        }
                        else
                        {
                            Console.WriteLine("failed to read h3 level");
                            GlobalVars.AttachedLevel = null;
                        }
                        break;

                    case "OD":

                        if (ReadProcessMemory(GlobalVars.GlobalProcessHandle, FindPointerAddy(GlobalVars.GlobalProcessHandle, GlobalVars.BaseAddress, GlobalVars.LoadedOffsets.OD_LevelName[Convert.ToInt32(GlobalVars.WinStoreFlag)]), buffer, buffer.Length, out bytesRead))
                        {
                            //levelstring here is laid out a little differently

                            holdstring = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                            //Console.WriteLine("Holdstring 1: " + holdstring);
                            holdstring = holdstring.Substring(0, holdstring.LastIndexOf("."));
                            //Console.WriteLine("Holdstring 2: " + holdstring);
                            //holdstring = holdstring.Substring(holdstring.LastIndexOf("\\") + 1);
                            // Console.WriteLine("Holdstring 3: " + holdstring);
                            char[] exceptions = new char[] { '_' };
                            holdstring = String.Concat(holdstring.Where(ch => Char.IsLetterOrDigit(ch) || exceptions?.Contains(ch) == true));
                            Console.WriteLine("read OD level: " + holdstring);
                            GlobalVars.AttachedLevel = holdstring;
                        }
                        else
                        {
                            Console.WriteLine("failed to read OD level");
                            GlobalVars.AttachedLevel = null;
                        }
                        break;

                    case "HR":
                        if (ReadProcessMemory(GlobalVars.GlobalProcessHandle, FindPointerAddy(GlobalVars.GlobalProcessHandle, GlobalVars.BaseAddress, GlobalVars.LoadedOffsets.HR_LevelName[Convert.ToInt32(GlobalVars.WinStoreFlag)]), buffer, buffer.Length, out bytesRead))
                        {
                            holdstring = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                            holdstring = holdstring.Substring(holdstring.LastIndexOf("\\") + 1);
                            holdstring = holdstring.Substring(holdstring.LastIndexOf("\\") + 1);
                            char[] exceptions = new char[] { '_' };
                            holdstring = String.Concat(holdstring.Where(ch => Char.IsLetterOrDigit(ch) || exceptions?.Contains(ch) == true));
                            Console.WriteLine("read hr level: " + holdstring);
                            GlobalVars.AttachedLevel = holdstring;
                        }
                        else
                        {
                            Console.WriteLine("failed to read hr level");
                            GlobalVars.AttachedLevel = null;
                        }
                        break;


                    case "H4":

                        if (ReadProcessMemory(GlobalVars.GlobalProcessHandle, FindPointerAddy(GlobalVars.GlobalProcessHandle, GlobalVars.BaseAddress, GlobalVars.LoadedOffsets.H4_LevelName[Convert.ToInt32(GlobalVars.WinStoreFlag)]), buffer, buffer.Length, out bytesRead))
                        {
                            holdstring = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                            holdstring = holdstring.Substring(holdstring.LastIndexOf("\\") + 1);
                            holdstring = holdstring.Substring(holdstring.LastIndexOf("\\") + 1);
                            char[] exceptions = new char[] { '_' };
                            holdstring = String.Concat(holdstring.Where(ch => Char.IsLetterOrDigit(ch) || exceptions?.Contains(ch) == true));
                            Console.WriteLine("read H4 level: " + holdstring);
                            GlobalVars.AttachedLevel = holdstring;
                        }
                        else
                        {
                            Console.WriteLine("failed to read H4 level");
                            GlobalVars.AttachedLevel = null;
                        }
                        break;

                }

                //let's set these ui values for funsies
                gametext.Text = "Game: " + GlobalVars.AttachedGame;
                leveltext.Text = "Levelcode: " + GlobalVars.AttachedLevel;

            }



            //Cool, we're actually done.

            //this is where we'll do our super frequent checking of MCC memory. explanations of how ReadProcessMemory works are in there.
            GetMCCValues();



        }




        private void GetMCCValues()
        {





            //for demonstration purposes, we'll just grab the current ingame tickcount for H1, and print a flag if the tickcount goes DOWN from one check to a next.  (eg level restart, or checkpoint revert)





            //to keep track of what the tickcount was on the last time we checked, we're going to store it in a global variable, CurrentTickCount.
            int oldTickCount = GlobalVars.CurrentTickCount;


            //now we need to get the current tickcount
            switch (GlobalVars.AttachedGame)
            {
                case "H1":

                    //we could add a check here if we're validly attached to the game or not by reading a part of the games memory for the expected value.
                    //I have a method, ValidCheck_H1(), that we could use for this. but I'll skip it for now since it might have a hella performance impact when mainloop runs super often.



                    //we need to create a byte array of the correct length which we'll feed into readprocessmemory. 4 bytes in this case since tickcount is an int.
                    byte[] buffer = new byte[4];
                    //let's explain how readprocessmemory works
                    if (ReadProcessMemory //ReadProcessMemory returns true on successful read, false on a failure. 
                        (GlobalVars.GlobalProcessHandle, //need to pass it the ProcessHandle
                        FindPointerAddy(GlobalVars.GlobalProcessHandle, GlobalVars.BaseAddress, GlobalVars.LoadedOffsets.H1_TickCounter[Convert.ToInt32(GlobalVars.WinStoreFlag)]),
                        //we need to tell readprocessmemory what address to read, as an IntPtr (which is basically an int). 
                        //of course what we have is a multi-level pointer, so we use our custom FindPointerAddy to resolve that to an address (the IntPtr).
                        //the multi-level-pointer is represented as an int[] array. but our loaded offsets have int[][]'s, this is to corrospond to the fact that we have different values for our pointers-
                        //depending whether we're attached to the steam version or winstore version. we use the winstoreflag to select between them. 


                        buffer, //pass it our buffer to use
                        buffer.Length, //tell it to only read the amount of memory corresponding to the length of our buffer
                        out int bytesRead)) //count of how many bytes were read (not really necessary)
                    {
                        //Sucess!
                        //now we can do some code if our readprocessmemory succeeded
                        GlobalVars.CurrentTickCount = BitConverter.ToInt32(buffer, 0);
                    }
                    else
                    {
                        //our readprocessmemory failed! this is a bad sign, we could throw an exception or whatever. 
                        //in this case I'm just going to return out of our GetMCCValues function since the rest probably won't work either.
                        return;
                    }

                    break;


                //could insert code for the other games here as required

                default:
                    return; //not h1, let's bail
                    break;

            }

            //cool, now we should have a current tickcount and previous tickcount, so to check if it's gone down (eg level restart, or checkpoint revert)

            if (oldTickCount > GlobalVars.CurrentTickCount)
            {
                Console.WriteLine("YOU DID THE THING!!");
            }








        }

        private static bool ValidCheck_H1()
        {

            try
            {
                byte[] buffer = new byte[6];
                if (ReadProcessMemory(GlobalVars.GlobalProcessHandle, FindPointerAddy(GlobalVars.GlobalProcessHandle, GlobalVars.BaseAddress, GlobalVars.LoadedOffsets.H1_CheckString[Convert.ToInt32(GlobalVars.WinStoreFlag)]), buffer, buffer.Length, out int bytesRead2))
                {
                    if (Encoding.UTF8.GetString(buffer, 0, buffer.Length) == "levels")
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

        }








        //these dictionaries aren't used anywhere in this stub program, but may be handy to you down the line. 

        readonly Dictionary<string, string> LevelCodeToNameH1 = new Dictionary<string, string>()
        { 
         // Halo 1
            //SP
            { "a10", "Pillar of Autumn" },
            { "a30", "Halo" },
            { "a50", "Truth and Rec" },
            { "b30", "Silent Cartographer" },
            { "b40", "AotCR" },
            { "c10", "343 Guilty Spark" },
            { "c20", "The Library" },
            { "c40", "Two Betrayals" },
            { "d20", "Keyes" },
            { "d40", "The Maw" },
            //MP
            { "beavercreek", "Battle Creek" },
            { "boardingaction", "Boarding Action" },
            { "bloodgulch", "Blood Gulch" },
            { "carousel", "Derelict" },
            { "chillout", "Chill Out" },
            { "damnation", "Damnation" },
            { "dangercanyon", "Danger Canyon" },
            { "deathisland", "Death Island" },
            { "gephyrophobia", "Gephyrophobia" },
            { "hangemhigh", "Hang 'Em High" },
            { "icefields", "Ice Fields" },
            { "infinity", "Infinity" },
            { "longest", "Longest" },
            { "prisoner", "Prisoner" },
            { "putput", "Chiron TL-34" },
            { "ratrace", "Rat Race" },
            { "sidewinder", "Sidewinder" },
            { "timberland", "Timberland" },
            { "wizard", "Wizard" },

        };

        readonly Dictionary<string, string> LevelCodeToNameH2 = new Dictionary<string, string>()
        { 

            // Halo 2
            //SP
            { "00a_introduction", "The Heretic" },
            { "01a_tutorial", "The Armory" },
            { "01b_spacestation", "Cairo Station" },
            { "03a_oldmombasa", "Outskirts" },
            { "03b_newmombasa", "Metropolis" },
            { "04a_gasgiant", "The Arbiter" },
            { "04b_floodlab", "The Oracle" },
            { "05a_deltaapproach", "Delta Halo" },
            { "05b_deltatowers", "Regret" },
            { "06a_sentinelwalls", "Sacred Icon" },
            { "06b_floodzone", "Quarantine Zone" },
            { "07a_highcharity", "Gravemind" },
            { "07b_forerunnership", "High Charity" },
            { "08a_deltacliffs", "Uprising" },
            { "08b_deltacontrol", "The Great Journey" },
            //MP
            { "ascension", "Ascension" },
            { "backwash", "Backwash" },
            { "beavercreek", "placeholder" },
            { "burial_mounds", "Burial Mounds" },
            { "coagulation", "Coagulation" },
            { "colossus", "Colossus" },
            { "containment", "Containment" },
            { "cyclotron", "Ivory Tower" },
            { "deltatap", "Sanctuary" },
            { "derelict", "Desolation" },
            { "dune", "Relic" },
            { "elongation", "Elongation" },
            { "foundation", "Foundation" },
            { "gemini", "Gemini" },
            { "headlong", "Headlong" },
            { "highplains", "Tombstone" },
            { "lockout", "Lockout" },
            { "midship", "Midship" },
            { "needle", "Uplift" },
            { "street_sweeper", "District" },
            { "triplicate", "Terminal" },
            { "turf", "Turf" },
            { "warlock", "Warlock" },
            { "waterworks", "Waterworks" },
            { "zanzibar", "Zanzibar" },

        };

        readonly Dictionary<string, string> LevelCodeToNameH3 = new Dictionary<string, string>()
        { 


             // Halo 3
            //SP
            { "005_intro", "Arrival" },
            { "010_jungle", "Sierra 117" },
            { "020_base", "Crow's Nest" },
            { "030_outskirts", "Tsavo Highway" },
            { "040_voi", "The Storm" },
            { "050_floodvoi", "Floodgate" },
            { "070_waste", "The Ark" },
            { "100_citadel", "The Covenant" },
            { "110_hc", "Cortana" },
            { "120_halo", "Halo" },
            { "130_epilogue", "Epilogue" },
            //MP
            { "armory", "Rat's Nest" },
            { "bunkerworld", "Standoff" },
            { "chill", "Narrows" },
            { "chillout", "Cold Storage" },
            { "construct", "Construct" },
            { "cyberdyne", "The Pit" },
            { "deadlock", "Highground" },
            { "descent", "Assembly" },
            { "docks", "Longshore" },
            { "fortress", "Citadel" },
            { "ghosttown", "Ghost Town" },
            { "guardian", "Guardian" },
            { "isolation", "Isolation" },
            { "lockout", "Blackout" },
            { "midship", "Heretic" },
            { "riverworld", "Valhalla" },
            { "salvation", "Epitaph" },
            { "sandbox", "Sandbox" },
            { "shrine", "Sandtrap" },
            { "sidewinder", "Avalanche" },
            { "snowbound", "Snowbound" },
            { "spacecamp", "Orbital" },
            { "warehouse", "Foundry" },
            { "zanzibar", "Last Resort" },

        };

        readonly Dictionary<string, string> LevelCodeToNameOD = new Dictionary<string, string>()
        { 


             // ODST
            //SP -- need to double check these. ODST is weird. also double check cases
            { "c100", "Prepare to Drop" }, //aka the cutscene, not the MS1 level
            { "c200", "Epilogue" },
            { "h100", "Mombasa Streets" },
            { "l200", "Data Hive" },
            { "l300", "Coastal Highway" },
            { "sc100", "Tayari Plaza" },
            { "sc110", "Uplift Reserve" },
            { "sc120", "Kizingo Boulevard" },
            { "sc130", "ONI Alpha Site" },
            { "sc140", "NMPD HQ" },
            { "sc150", "Kikowani Station" },
            //MP
            //imagine

        };

        readonly Dictionary<string, string> LevelCodeToNameHR = new Dictionary<string, string>()
        {

            // Halo Reach
            //SP
            { "m05", "Noble Actual" },
            { "m10", "Winter Contingency" },
            { "m20", "ONI: Sword Base" },
            { "m30", "Nightfall" },
            { "m35", "Tip of the Spear" },
            { "m45", "Long Night of Solace" },
            { "m50", "Exodus" },
            { "m52", "New Alexandria" },
            { "m60", "The Package" },
            { "m70", "The Pillar of Autumn" },
            { "m70_a", "Credits" },
            { "m70_bonus", "Lone Wolf" },
            //MP
            { "20_sword_slayer", "Sword Base" },
            { "30_settlement", "Powerhouse" },
            { "35_island", "Spire" },
            { "45_aftship", "Zealot" },
            { "45_launch_station", "Countdown" },
            { "50_panopticon", "Boardwalk" },
            { "52_ivory_tower", "Reflection" },
            { "70_boneyard", "Boneyard" },
            { "forge_halo", "Forge World" },
            { "dlc_invasion ", "Breakpoint" },
            { "dlc_medium ", "Tempest" },
            { "dlc_slayer ", "Anchor 9" },
            { "cex_beaver_creek ", "Battle Canyon" },
            { "cex_damnation ", "Penance" },
            { "cex_ff_halo ", "Installation 04" },
            { "cex_hangemhigh ", "High Noon" },
            { "cex_headlong ", "Breakneck" },
            { "cex_prisoner ", "Solitary" },
            { "cex_timberland ", "Ridgeline" },
            { "condemned ", "Condemned" },
            { "ff_unearthed ", "Unearthed" },
            { "trainingpreserve ", "Highlands" },
            { "ff10_prototype ", "Overlook" },
            { "ff20_courtyard ", "Courtyard" },
            { "ff30_waterfront ", "Waterfront" },
            { "ff45_corvette ", "Corvette" },
            { "ff50_park ", "Beachhead" },
            { "ff60_airview ", "Outpost" },
            { "ff60 icecave ", "Glacier" },
            { "ff70_holdout ", "Holdout" },

        };


        readonly Dictionary<string, string> LevelCodeToNameH4 = new Dictionary<string, string>()
        { 


             // Halo 4
            //SP
            { "m05_prologue", "Prologue" },
            { "m10_crash", "Dawn" },
            { "m020", "Requiem" },
            { "m30_cryptum", "Forerunner" },
            { "m40_invasion", "Reclaimer" },
            { "m60_rescue", "Infinity" },
            { "m70_liftoff", "Shutdown" },
            { "m80_delta", "Composer" },
            { "m90_sacrifice", "Midnight" },
            { "m95_epilogue", "Epilogue" },
            //Spartan Ops
            { "ff87_chopperbowl", "Quarry" },
            { "ff86_sniperalley", "Sniper Alley" },
            { "ff90_fortsw", "Fortress" },
            { "ff84_temple", "The Refuge" },
            { "ff82_scurve", "The Cauldron" },
            { "ff81_courtyard", "The Gate" },
            { "ff91_complex", "The Galileo" },
            { "ff92_valhalla", "Two Giants" },
            { "dlc01_factory", "Lockup" },
            { "ff151_mezzanine", "Control" },
            { "ff153_caverns", "Warrens" },
            { "ff152_vortex", "Cyclone" },
            { "ff155_breach", "Harvester" },
            { "ff154_hillside", "Apex" },
            { "dlc01_engine", "Infinity" },
            //MP -- I haven't double checked that all of these are correct
            { "ca_blood_cavern", "Abandon" },
            { "ca_blood_crash", "Exile" },
            { "ca_canyon", "Meltdown" },
            { "ca_forge_bonzanza", "Impact" },
            { "ca_forge_erosion", "Erosion" },
            { "ca_forge_ravine", "Ravine" },
            { "ca_gore_valley", "Longbow" },
            { "ca_redoubt", "Vortex" },
            { "ca_tower", "Solace" },
            { "ca_warhouse", "Adrift" },
            { "ca_wraparound", "Haven" },
            { "dlc_forge_island", "Forge Island" },
            { "dlc dejewel", "Shatter" },
            { "dlc dejunkyard", "Wreckage" },
            { "z05_cliffside", "Complex" },
            { "zd_02_grind", "Harvest" },
            { "ca deadlycrossing", "Monolith" },
            { "ca_port", "Landfall" },
            { "ca_rattler", "Skyline" },
            { "ca_basin", "Outcast" },
            { "ca_highrise", "Perdition" },
            { "ca_spiderweb", "Daybreak" },
            { "ca_creeper", "Pitfall" },
            { "ca_dropoff", "Daybreak" }, //nfi why there's two daybreaks

        };

        private void StartRecording_Click(object sender, RoutedEventArgs e)
        {
            obs.StartRecording();
        }

        private void ConnectOBS_Click(object sender, RoutedEventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                connectOBSButton.Content = "Connecting...";
            });
            try
            {
                obs.Connect("ws://127.0.0.1:4444", wsPasswordField.Password);
            }
            catch (AuthFailureException)
            {
                this.Dispatcher.Invoke(() =>
                {
                    connectOBSButton.Content = "OBS Authentication Failed!";
                });
                Console.WriteLine("OBS Websocket Authentication failed.");
                return;
            }
            catch (ErrorResponseException ex)
            {
                this.Dispatcher.Invoke(() =>
                {
                    connectOBSButton.Content = "OBS Connection Failed!";
                });
                Console.WriteLine("OBS Websocket Connection failed: " + ex.Message);
                return;
            }
        }

        private void onConnect(object sender, EventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                connectOBSButton.Content = "Connected!";
                recordingButton.IsEnabled = true;
                connectOBSButton.IsEnabled = false;
            });
        }

        private void onDisconnect(object sender, EventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                connectOBSButton.Content = "Connect to OBS";
                recordingButton.IsEnabled = false;
                connectOBSButton.IsEnabled = true;
            });
        }
    }
}