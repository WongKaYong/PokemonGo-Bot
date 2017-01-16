using System;
using System.Threading.Tasks;
using PokemonGo.RocketAPI.Exceptions;
using System.Reflection;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.IO;
using PokemonGo.RocketAPI.Console.Helper;
using PokemonGo.RocketAPI.Logic.Utils;
using POGOProtos.Enums;
using System.Device.Location;
using System.Collections.ObjectModel;
using Google.Protobuf;
using System.Runtime.InteropServices;

namespace PokemonGo.RocketAPI.Console
{
    internal class Program
    {
        [DllImport("kernel32.dll")]
        public static extern Boolean FreeConsole();
        
        public static string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs");
        public static string path_translation = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Translations");
        public static string path_device = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Device");
        public static string lastcords = Path.Combine(path, "LastCoords.txt");
        public static string huntstats = Path.Combine(path, "HuntStats.txt");
        public static string deviceSettings = Path.Combine(path_device, "DeviceInfo.txt");
        public static string cmdCoords = string.Empty;
        public static string accountProfiles = Path.Combine(path, "Profiles.txt");
        static string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        public static string pokelog = Path.Combine(logPath, "PokeLog.txt");
        public static string manualTransferLog = Path.Combine(logPath, "TransferLog.txt");
        public static string EvolveLog = Path.Combine(logPath, "EvolveLog.txt");
        public static string path_pokedata = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PokeData");       
        
        static void SharePokesniperURI(string uri)
        {
            try 
            {
                var filename = Path.GetTempPath()+"pokesniper";
                if (File.Exists(filename)){
                    MessageBox.Show("There is a pending pokemon.\nTry latter");
                }
                var stream = new FileStream(filename,FileMode.OpenOrCreate);
                var writer = new BinaryWriter(stream,new UTF8Encoding());
                writer.Write(uri);
                stream.Close();
            } 
            catch (Exception e) 
            {
                MessageBox.Show(e.ToString());
            }
        }
        [STAThread]
        static void Main(string[] args)
        {
            if ( args.Length > 0)
            {
                if (args[0].Contains("pokesniper2"))
                {
                    SharePokesniperURI(args[0]);
                    return;
                }
            }          
            SleepHelper.PreventSleep();
            if (args != null && args.Length > 0)
            {
                foreach (string arg in args)
                {
                    if (arg.Contains(","))
                    {
                        Logger.ColoredConsoleWrite(ConsoleColor.Green, $"Found coordinates in command line: {arg}");
                        if (File.Exists(lastcords))
                        {
                            Logger.ColoredConsoleWrite(ConsoleColor.Yellow, "Last coords file exists, trying to delete it");
                            File.Delete(lastcords);
                        }
                        cmdCoords = arg;
                    }
                }
            }
            if (!Directory.Exists(logPath))
            {
                Directory.CreateDirectory(logPath);
            }
            if (!File.Exists(pokelog))
            {
                File.Create(pokelog).Close();
            }
            if (!File.Exists(manualTransferLog))
            {
                File.Create(manualTransferLog).Close();
            }
            if (!File.Exists(EvolveLog))
            {
                File.Create(EvolveLog).Close();
            }
            var openGUI = false;
            if (args != null && args.Length > 0 && args[0].Contains("-nogui"))
            {
                Logger.ColoredConsoleWrite(ConsoleColor.Red, "You added -nogui! If you didnt setup correctly with the GUI. It wont work.");

                //TODO Implement JSON Load

                if (Globals.usePwdEncryption)
                {
                    Globals.password = Encryption.Decrypt(Globals.password);
                }

                if (cmdCoords != string.Empty)
                {
                    string[] crdParts = cmdCoords.Split(',');
                    Globals.latitute = double.Parse(crdParts[0].Replace(',', '.'), GUI.cords, System.Globalization.NumberFormatInfo.InvariantInfo);
                    Globals.longitude = double.Parse(crdParts[1].Replace(',', '.'), GUI.cords, System.Globalization.NumberFormatInfo.InvariantInfo);
                }
                Logger.ColoredConsoleWrite(ConsoleColor.Yellow, $"Starting at: {Globals.latitute},{Globals.longitude}");
            }
            else
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new GUI());
                openGUI = Globals.pokeList;
            }

            //Logger.SetLogger(new Logging.ConsoleLogger(LogLevel.Info));

            Globals.infoObservable.HandleNewHuntStats += SaveHuntStats;

            Task.Run(() =>
            {

                CheckVersion();
                try
                {
                    new Logic.Logic(new Settings(), Globals.infoObservable).Execute();
                }
                catch (PtcOfflineException)
                {
                    Logger.ColoredConsoleWrite(ConsoleColor.Red, "PTC Servers are probably down OR you credentials are wrong.", LogLevel.Error);
                    Logger.ColoredConsoleWrite(ConsoleColor.Red, "Trying again in 20 seconds...");
                    Thread.Sleep(20000);
                    new Logic.Logic(new Settings(), Globals.infoObservable).Execute();
                }
                catch (AccountNotVerifiedException)
                {
                    Logger.ColoredConsoleWrite(ConsoleColor.Red, "Your PTC Account is not activated. Exiting in 10 Seconds.");
                    Thread.Sleep(10000);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Logger.ColoredConsoleWrite(ConsoleColor.Red, $"Unhandled exception: {ex}", LogLevel.Error);
                    Logger.Error("Restarting in 20 Seconds.");
                    Thread.Sleep(20000);
                    new Logic.Logic(new Settings(), Globals.infoObservable).Execute();
                }
            });
            if (openGUI)
            {
                if (Globals.simulatedPGO)
                {

                    Application.Run( new GameAspectSimulator());
                }
                else
                {
                    if (Globals.consoleInTab)
                        FreeConsole();
                    Application.Run( new Pokemons());
                }
            }
            else
            {
                   System.Console.ReadLine();
            }
            SleepHelper.AllowSleep();
        }
        private static void SaveHuntStats(string newHuntStat)
        {
            File.AppendAllText(huntstats, newHuntStat);
        }
        public static void CheckVersion()
        {
            try
            {
                var match =
                    new Regex(
                        @"\[assembly\: AssemblyVersion\(string.Empty(\d{1,})\.(\d{1,})\.(\d{1,})\.(\d{1,})string.Empty\)\]")
                        .Match(DownloadServerVersion());

                if (!match.Success) return;
                var gitVersion =
                    new Version(
                        $"{match.Groups[1]}.{match.Groups[2]}.{match.Groups[3]}.{match.Groups[4]}");
                if (gitVersion <= Assembly.GetExecutingAssembly().GetName().Version)
                {
                    //ColoredConsoleWrite(ConsoleColor.Yellow, "Awesome! You have already got the newest version! " + Assembly.GetExecutingAssembly().GetName().Version);
                    return;
                }

                Logger.ColoredConsoleWrite(ConsoleColor.Red, "There is a new Version available: " + gitVersion);
                Logger.ColoredConsoleWrite(ConsoleColor.Red, "Its recommended to use the newest Version.");
                if (cmdCoords == string.Empty)
                {
                    Logger.ColoredConsoleWrite(ConsoleColor.Red, "Starting in 10 Seconds.");
                    Thread.Sleep(10000);
                }
                else
                {
                    Logger.ColoredConsoleWrite(ConsoleColor.Yellow, "Starting right away because we are probably sniping.");
                }
            }
            catch (Exception)
            {
                Logger.ColoredConsoleWrite(ConsoleColor.White, "Unable to check for updates now...");
            }
        }
        public static Version getNewestVersion()
        {
            try
            {
                var match = DownloadServerVersion();

                var gitVersion = new Version(match);

                return gitVersion;

            }
            catch (Exception)
            {
                return Assembly.GetExecutingAssembly().GetName().Version;
            }
        }
        public static string DownloadServerVersion()
        {
            using (var wC = new WebClient())
                return
                    wC.DownloadString(
                        "https://raw.githubusercontent.com/Ar1i/PokemonGo-Bot/master/ver.md");
        }       
    }
    public static class ManualSnipePokemon
    {
        public static PokemonId? ID = null;
        public static GeoCoordinate Location = null;
        public static int secondsSnipe = 2;
        public static int triesSnipe = 3;
    }
    public static class Globals
    {
        // Bot Info  Globals (not yet implemented in any function)
        public static readonly string BotVersion = "1.0.0";
        public static readonly bool BotDebugFlag = true;
        public static readonly bool BotStableFlag = false;

        // Other Globals
        public static Collection<Profile> Profiles = new Collection<Profile>();
        public static string pFHashKey;
        public static string ProfileName = "DefaultProfile";
        public static bool IsDefault = false;
        public static int RunOrder = 0;
        public static string SettingsJSON = "";
        public static Enums.AuthType acc = Enums.AuthType.Google;
        public static string email = "empty";
        public static string password = "empty";
        public static bool defLoc = true;
        public static bool uselastcoords = true;
        public static double latitute = 40.764883;
        public static double longitude = -73.972967;
        public static double altitude = 15.173855;
        public static double speed = 15;
        public static int MinWalkSpeed = 5;
        public static int radius = 5000;
        public static bool transfer = true;
        public static int duplicate = 3;
        public static bool evolve = true;
        public static int maxCp = 999;
        public static int excellentthrow = 25;
        public static int greatthrow = 25;
        public static int nicethrow = 25;
        public static int ordinarythrow = 25;
        public static int pokeball = 100;
        public static int greatball = 100;
        public static int ultraball = 100;
        public static int revive = 100;
        public static int potion = 100;
        public static int superpotion = 100;
        public static int hyperpotion = 100;
        public static int toppotion = 100;
        public static int toprevive = 100;
        public static int berry = 100;
        public static int MinCPforGreatBall = 500;
        public static int MinCPforUltraBall = 1000;
        public static int ivmaxpercent = 0;
        public static bool _pauseTheWalking = false;
        private static bool _pauseAtWalking = false;
        public static bool pauseAtWalking
        {
            get
            {
                return _pauseAtWalking;
            }
            set
            {
                if (Logic.Logic.Instance != null)
                {
                    Logic.Logic.Instance.PauseWalking = value;
                    _pauseAtWalking = value;
                }
            }
        }
        public static bool LimitPokeballUse = false;
        public static bool LimitGreatballUse = false;
        public static bool LimitUltraballUse = false;
        public static bool NextBestBallOnEscape = false;
        public static int Max_Missed_throws = 3;
        public static List<PokemonId> noTransfer;
        public static List<PokemonId> noCatch;
        public static List<PokemonId> doEvolve;
        public static List<PokemonId> NotToSnipe;
        public static string telAPI = string.Empty;
        public static string telName = string.Empty;
        public static int telDelay = 5000;
        public static bool pauseAtPokeStop = false;
        public static bool farmPokestops = true;
        public static bool CatchPokemon = true;
        public static bool BreakAtLure = false;
        public static bool UseAnimationTimes = true;
        public static bool UseLureAtBreak = false;
        public static bool UseGoogleMapsAPI = false;
        public static string GoogleMapsAPIKey;
        public static bool RandomReduceSpeed = false;
        public static bool UseBreakFields = false;
        public static double TimeToRun;
        public static int PokemonCatchLimit = 1000;
        public static int PokestopFarmLimit = 2000;
        public static int XPFarmedLimit = 150000;
        public static int BreakInterval = 0;
        public static int BreakLength = 0;
        public static int navigation_option = 1;
        public static bool useluckyegg = true;
        public static bool useincense = true;
        public static bool userazzberry = true;
        public static double razzberry_chance = 0.35;
        public static bool pokeList = true;
        public static bool consoleInTab = false;
        public static bool keepPokemonsThatCanEvolve = true;
        public static bool TransferFirstLowIV = true;
        public static bool pokevision = false;
        public static bool useLuckyEggIfNotRunning = false;
        public static bool autoIncubate = true;
        public static bool useBasicIncubators = false;
        public static bool sleepatpokemons = true;
        public static string settingsLanguage = "en";
        public static Logic.LogicInfoObservable infoObservable = new Logic.LogicInfoObservable();
        public static bool Espiral = false;
        public static bool MapLoaded = false;
        public static bool logPokemons = false;
        public static LinkedList<GeoCoordinate> NextDestinationOverride = new LinkedList<GeoCoordinate>();
        public static LinkedList<GeoCoordinate> RouteToRepeat = new LinkedList<GeoCoordinate>();
        public static bool RepeatUserRoute = false;
        public static bool logManualTransfer = false;
        public static bool UseLureGUIClick = false;
        public static bool UseLuckyEggGUIClick = false;
        public static bool UseIncenseGUIClick = false;
        public static bool RelocateDefaultLocation = false;
        public static double RelocateDefaultLocationTravelSpeed = 0;
        public static bool bLogEvolve = false;
        public static bool LogEggs = false;
        public static bool pauseAtEvolve = false;
        public static bool pauseAtEvolve2 = false;
        public static bool AutoUpdate = false;
        public static bool usePwdEncryption = false;
        public static bool CheckWhileRunning = false;
        internal static int InventoryBasePokeball = 10;
        internal static int InventoryBaseGreatball = 10;
        internal static int InventoryBaseUltraball = 10;
        internal static bool SnipePokemon;
        internal static bool FirstLoad;
        public static int MinCPtoCatch = 0;
        public static int MinIVtoCatch = 0;
        public static bool AvoidRegionLock = true;
        public static bool ForceSnipe = false;
        public static bool simulatedPGO = false;
        public static ByteString SessionHash;

        public static bool No2kmEggs = false;
        public static bool No5kmEggs = false;
        public static bool No10kmEggs = false;
        public static bool EggsAscendingSelection = true;

        public static bool No2kmEggsBasicInc = false;
        public static bool No5kmEggsBasicInc = false;
        public static bool No10kmEggsBasicInc = false;
        public static bool EggsAscendingSelectionBasicInc = false;

        public static bool EnableVerboseLogging = false;

        public static bool farmGyms;
        public static bool CollectDailyBonus;
    }
}
