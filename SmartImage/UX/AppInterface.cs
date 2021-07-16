﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using JetBrains.Annotations;
using Novus.Utilities;
using Novus.Win32;
using SimpleCore.Cli;
using SimpleCore.Net;
using SimpleCore.Utilities;
using SmartImage.Core;
using SmartImage.Lib.Engines;
using SmartImage.Lib.Searching;
using SmartImage.Lib.Utilities;
using SmartImage.Utilities;
using static Novus.Utilities.ReflectionOperatorHelpers;
using static SmartImage.Program;

// ReSharper disable UnusedMember.Global
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming

// ReSharper disable PossibleNullReferenceException
#pragma warning disable CA1069
#pragma warning disable CA2101

namespace SmartImage.UX
{
	/// <summary>
	/// Handles the main menu interface
	/// </summary>
	internal static class AppInterface
	{
		/*
		 * TODO
		 *
		 */

		#region Colors

		internal static readonly Color ColorMain  = Color.Yellow;
		internal static readonly Color ColorOther = Color.Aquamarine;
		internal static readonly Color ColorYes   = Color.GreenYellow;
		internal static readonly Color ColorNo    = Color.Red;

		private static readonly Dictionary<SearchEngineOptions, Color> EngineColorMap = new()
		{
			{SearchEngineOptions.Iqdb, Color.Pink},
			{SearchEngineOptions.SauceNao, Color.SpringGreen},
			{SearchEngineOptions.Ascii2D, Color.NavajoWhite},
			{SearchEngineOptions.Bing, Color.DeepSkyBlue},
			{SearchEngineOptions.GoogleImages, Color.FloralWhite},
			{SearchEngineOptions.ImgOps, Color.Gray},
			{SearchEngineOptions.KarmaDecay, Color.IndianRed},
			{SearchEngineOptions.Tidder, Color.Orange},
			{SearchEngineOptions.TraceMoe, Color.MediumSlateBlue},
			{SearchEngineOptions.Yandex, Color.OrangeRed},
			{SearchEngineOptions.TinEye, Color.CornflowerBlue},
		};

		#endregion


		#region Elements

		// NOTE: Do not reorder

		public const string Description = "Press the result number to open in browser\n" +
		                                  "Ctrl: Load direct | Alt: Show other | Shift: Open raw | Alt+Ctrl: Download";

		private static readonly string Enabled = StringConstants.CHECK_MARK.ToString().AddColor(ColorYes);

		private static readonly string Disabled = StringConstants.MUL_SIGN.ToString().AddColor(ColorNo);

		internal static string ToToggleString(this bool b) => b ? Enabled : Disabled;

		private static string GetName(string s, bool added) => $"{s} ({(added.ToToggleString())})";

		#endregion

		private static readonly NConsoleOption[] MainMenuOptions =
		{
			new()
			{
				Name  = ">>> Run <<<",
				Color = ColorMain,
				Function = () =>
				{
					ImageQuery query = NConsole.ReadInput("Image file or direct URL", x =>
					{
						x = x.CleanString();

						(bool url, bool file) = ImageQuery.IsUriOrFile(x);
						return !(url || file);
					}, "Input must be file or direct image link");

					Config.Query = query;
					return true;
				}
			},


			CreateConfigOption(memberof(() => Config.SearchEngines), "Engines"),
			CreateConfigOption(memberof(() => Config.PriorityEngines), "Priority engines"),

			CreateConfigOption(propertyof(() => Config.Filtering), "Filter", 3),
			CreateConfigOption(propertyof(() => Config.Notification), "Notification", 4),
			CreateConfigOption(propertyof(() => Config.NotificationImage), "Notification image", 5),

			CreateConfigOption(propertyof(() => AppIntegration.IsContextMenuAdded), "Context menu", 6,
			                   (added) => AppIntegration.HandleContextMenu(
				                   added ? IntegrationOption.Remove : IntegrationOption.Add)),

			new()
			{
				Name = "Config",
				Function = () =>
				{
					//Console.Clear();

					Console.WriteLine(Config);
					NConsole.WaitForInput();

					return null;
				}
			},
			new()
			{
				Name = "Info",
				Function = () =>
				{
					//Console.Clear();

					Console.WriteLine($"Author: {AppInfo.Author}");

					Console.WriteLine($"Current version: {AppInfo.Version} ({UpdateInfo.GetUpdateInfo().Status})");
					Console.WriteLine($"Latest version: {ReleaseInfo.GetLatestRelease()}");

					Console.WriteLine();

					var di = new DirectoryInfo(AppInfo.ExeLocation);

					Console.WriteLine($"Executable location: {di.Parent.Name}");
					Console.WriteLine($"In path: {AppInfo.IsAppFolderInPath}");

					Console.WriteLine();
					Console.WriteLine(Strings.Separator);

					foreach (var utility in ImageHelper.UtilitiesMap) {
						Console.WriteLine(utility);
					}

					Console.WriteLine();
					Console.WriteLine(Strings.Separator);

					var dependencies = ReflectionHelper.DumpDependencies();

					foreach (var name in dependencies) {
						Console.WriteLine($"{name.Name} ({name.Version})");
					}

					NConsole.WaitForInput();


					return null;
				}
			},
			new()
			{
				Name = "Update",
				Function = () =>
				{
					UpdateInfo.AutoUpdate();

					return null;
				}
			},
			new()
			{
				Name = "Help",
				Function = () =>
				{
					WebUtilities.OpenUrl(AppInfo.Wiki);

					return null;
				}
			},
#if DEBUG


			new()
			{
				Name = "Debug",
				Function = () =>
				{
					Config.Query = @"C:\Users\Deci\Pictures\Test Images\Test1.jpg";
					return true;
				}
			},
#endif

		};


		public static readonly NConsoleDialog MainMenuDialog = new()
		{
			Options = MainMenuOptions,
			Header  = AppInfo.NAME_BANNER
		};

		#region Config option

		private static NConsoleOption CreateConfigOption(MemberInfo m, string name)
		{
			return new()
			{
				Name  = name,
				Color = ColorOther,
				Function = () =>
				{
					var enumOptions = NConsoleOption.FromEnum<SearchEngineOptions>();

					var selected = NConsole.ReadOptions(new NConsoleDialog
					{
						Options        = enumOptions,
						SelectMultiple = true
					});

					var enumValue = Enums.ReadFromSet<SearchEngineOptions>(selected);
					var field     = Config.GetType().GetResolvedField((m).Name);
					field.SetValue(Config, enumValue);

					Console.WriteLine(enumValue);

					NConsole.WaitForSecond();

					Debug.Assert((SearchEngineOptions) field.GetValue(Config) == enumValue);

					UpdateConfig();

					return null;
				}
			};
		}

		private static NConsoleOption CreateConfigOption(PropertyInfo member, string name, int i, Action<bool> fn)
		{
			bool initVal = (bool) member.GetValue(Config);

			return new NConsoleOption()
			{
				Name = GetName(name, initVal),
				Function = () =>
				{
					var pi = member.DeclaringType.GetProperty(member.Name);

					bool curVal = (bool) pi.GetValue(null);

					fn(curVal);

					bool newVal = (bool) pi.GetValue(null);

					MainMenuOptions[i].Name = GetName(name, newVal);

					Debug.Assert((bool) pi.GetValue(null) == newVal);

					return null;
				}
			};
		}

		private static NConsoleOption CreateConfigOption(PropertyInfo member, string name, int i)
		{
			bool initVal = (bool) member.GetValue(Config);

			return new NConsoleOption()
			{
				Name = GetName(name, initVal),
				Function = () =>
				{
					var    fi     = Config.GetType().GetResolvedField(member.Name);
					object curVal = fi.GetValue(Config);
					bool   newVal = !(bool) curVal;
					fi.SetValue(Config, newVal);

					MainMenuOptions[i].Name = GetName(name, newVal);

					Debug.Assert((bool) fi.GetValue(Config) == newVal);

					UpdateConfig();

					return null;
				}
			};
		}

		#endregion

		#region Result option

		public static NConsoleOption CreateResultOption(SearchResult result)
		{
			var color = EngineColorMap[result.Engine.EngineOption];

			var option = new NConsoleOption
			{
				Function = CreateOpenFunction(result.PrimaryResult is {Url: { }}
					? result.PrimaryResult.Url
					: result.RawUri),

				AltFunction = () =>
				{
					if (result.OtherResults.Any()) {

						var options = CreateResultOptions(result.OtherResults, $"Other result");

						NConsole.ReadOptions(new NConsoleDialog
						{
							Options = options
						});
					}

					return null;
				},

				ComboFunction = CreateDownloadFunction(result.PrimaryResult),
				ShiftFunction = CreateOpenFunction(result.RawUri),

				Color = color,

				Name = result.Engine.Name,
				Data = result,
			};

			option.CtrlFunction = () =>
			{
				var cts = new CancellationTokenSource();

				NConsoleProgress.Queue(cts);

				result.OtherResults.AsParallel().ForAll(f => f.FindDirectImages());

				result.PrimaryResult = result.OtherResults.First();


				cts.Cancel();
				cts.Dispose();

				option.Data = result;

				return null;
			};


			return option;
		}

		[StringFormatMethod("n")]
		public static NConsoleOption[] CreateResultOptions(IEnumerable<ImageResult> result, string n, Color c = default)
		{
			if (c == default) {
				c = ColorOther;
			}

			int i = 0;
			return result.Select(r => CreateResultOption(r, $"{n} #{i++}", c)).ToArray();
		}

		internal static NConsoleOption CreateResultOption(ImageResult result, string n, Color c,
		                                                  float correction = -.3f)
		{
			var option = new NConsoleOption
			{
				Function      = CreateOpenFunction(result.Url),
				ComboFunction = CreateDownloadFunction(result),
				Color         = c.ChangeBrightness(correction),
				Name          = n,
				Data          = result
			};

			return option;
		}

		internal static NConsoleFunction CreateOpenFunction(Uri url)
		{
			return () =>
			{
				if (url != null) {
					WebUtilities.OpenUrl(url.ToString());
				}

				return null;
			};
		}

		internal static NConsoleFunction CreateDownloadFunction(ImageResult result)
		{
			return () =>
			{
				var direct = result.Direct;


				if (direct != null) {

					string filename = Path.GetFileName(direct.AbsolutePath);

					var path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

					string combine = Path.Combine(path, filename);

					using var wc = new WebClient();

					wc.DownloadFile(direct, combine);

					FileSystem.ExploreFile(combine);

					Debug.WriteLine($"Download: {combine}");
				}

				return null;
			};
		}

		#endregion

		private static void UpdateConfig()
		{
			Client.Reload();
			AppConfig.SaveConfigFile();
		}


		#region Native

		/*
		/// <summary>Returns true if the current application has focus, false otherwise</summary>
		internal static bool ApplicationIsActivated()
		{
			//https://stackoverflow.com/questions/7162834/determine-if-current-application-is-activated-has-focus
			var activatedHandle = Native.GetForegroundWindow();

			if (activatedHandle == IntPtr.Zero) {
				return false; // No window is currently activated
			}

			var p1     = Process.GetCurrentProcess();
			var procId = p1.Id;
			int activeProcId;
			Native.GetWindowThreadProcessId(activatedHandle, out activeProcId);
			var p2 = Process.GetProcessById(activeProcId);


			return activeProcId == procId;
		}*/


		[DllImport(Native.USER32_DLL)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

		[DllImport(Native.USER32_DLL)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetForegroundWindow(IntPtr hWnd);

		internal static void FlashWindow(IntPtr hWnd)
		{
			var fInfo = new FLASHWINFO
			{
				cbSize    = (uint) Marshal.SizeOf<FLASHWINFO>(),
				hwnd      = hWnd,
				dwFlags   = FlashWindowType.FLASHW_ALL,
				uCount    = 8,
				dwTimeout = 75,

			};


			FlashWindowEx(ref fInfo);
		}

		internal static void FlashConsoleWindow()  => FlashWindow(Native.GetConsoleWindow());
		internal static void BringConsoleToFront() => SetForegroundWindow(Native.GetConsoleWindow());

		[DllImport(Native.USER32_DLL)]
		internal static extern MessageBoxResult MessageBox(IntPtr hWnd, string text, string caption,
		                                                   MessageBoxOptions options);

		#endregion
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct FLASHWINFO
	{
		public uint            cbSize;
		public IntPtr          hwnd;
		public FlashWindowType dwFlags;
		public uint            uCount;
		public int             dwTimeout;
	}

	internal enum FlashWindowType : uint
	{
		/// <summary>
		/// Stop flashing. The system restores the window to its original state.
		/// </summary>    
		FLASHW_STOP = 0,

		/// <summary>
		/// Flash the window caption
		/// </summary>
		FLASHW_CAPTION = 1,

		/// <summary>
		/// Flash the taskbar button.
		/// </summary>
		FLASHW_TRAY = 2,

		/// <summary>
		/// Flash both the window caption and taskbar button.
		/// This is equivalent to setting the <see cref="FLASHW_CAPTION"/> | <see cref="FLASHW_TRAY"/> flags.
		/// </summary>
		FLASHW_ALL = 3,

		/// <summary>
		/// Flash continuously, until the <seealso cref="FLASHW_STOP"/> flag is set.
		/// </summary>
		FLASHW_TIMER = 4,

		/// <summary>
		/// Flash continuously until the window comes to the foreground.
		/// </summary>
		FLASHW_TIMERNOFG = 12
	}

	/// <summary>
	/// Represents possible values returned by the <see cref="AppInterface.MessageBox"/> function.
	/// </summary>
	internal enum MessageBoxResult : uint
	{
		/// <summary>
		/// The OK button was selected.
		/// </summary>
		IDOK = 1,

		/// <summary>
		/// The Cancel button was selected.
		/// </summary>
		IDCANCEL = 2,

		/// <summary>
		/// The Abort button was selected.
		/// </summary>
		IDABORT = 3,

		/// <summary>
		/// The Retry button was selected.
		/// </summary>
		IDRETRY = 4,

		/// <summary>
		/// The Ignore button was selected.
		/// </summary>
		IDIGNORE = 5,

		/// <summary>
		/// The Yes button was selected.
		/// </summary>
		IDYES = 6,

		/// <summary>
		/// The No button was selected.
		/// </summary>
		IDNO = 7,

		/// <summary>
		/// The user closed the message box.
		/// </summary>
		IDCLOSE = 8,

		/// <summary>
		/// The Help button was selected.
		/// </summary>
		IDHELP = 9,

		/// <summary>
		/// The Try Again button was selected.
		/// </summary>
		IDTRYAGAIN = 10,

		/// <summary>
		/// The Continue button was selected.
		/// </summary>
		IDCONTINUE = 11,

		/// <summary>
		/// The user did not click any button and the messagebox timed out.
		/// </summary>
		IDTIMEOUT = 32000,
	}

	/// <summary>
	/// Flags that define appearance and behavior of a standard message box
	/// displayed by a call to the <see cref="MessageBox"/> function.
	/// </summary>
	[Flags]
	internal enum MessageBoxOptions : uint
	{
		/// <summary>
		/// The message box contains one push button: OK. This is the default.
		/// </summary>
		MB_OK = 0x000000,

		/// <summary>
		/// The message box contains two push buttons: OK and Cancel.
		/// </summary>
		MB_OKCANCEL = 0x000001,

		/// <summary>
		/// The message box contains three push buttons: Abort, Retry, and Ignore.
		/// </summary>
		MB_ABORTRETRYIGNORE = 0x000002,

		/// <summary>
		/// The message box contains three push buttons: Yes, No, and Cancel.
		/// </summary>
		MB_YESNOCANCEL = 0x000003,

		/// <summary>
		/// The message box contains two push buttons: Yes and No.
		/// </summary>
		MB_YESNO = 0x000004,

		/// <summary>
		/// The message box contains two push buttons: Retry and Cancel.
		/// </summary>
		MB_RETRYCANCEL = 0x000005,

		/// <summary>
		/// The message box contains three push buttons: Cancel, Try Again, Continue. Use this message box type instead of <see cref="MB_ABORTRETRYIGNORE"/>.
		/// </summary>
		MB_CANCELTRYCONTINUE = 0x000006,

		/// <summary>
		/// A stop-sign icon appears in the message box.
		/// </summary>
		MB_ICONSTOP = 0x000010,

		/// <summary>
		/// A stop-sign icon appears in the message box.
		/// </summary>
		MB_ICONERROR = 0x00000010,

		/// <summary>
		/// A stop-sign icon appears in the message box.
		/// </summary>
		MB_ICONHAND = 0x00000010,

		/// <summary>
		/// A question-mark icon appears in the message box. The question-mark message icon is no longer recommended because it does not clearly represent a specific type of message
		/// and because the phrasing of a message as a question could apply to any message type.
		/// In addition, users can confuse the message symbol question mark with Help information.
		/// Therefore, do not use this question mark message symbol in your message boxes.
		/// The system continues to support its inclusion only for backward compatibility.
		/// </summary>
		MB_ICONQUESTION = 0x000020,

		/// <summary>
		/// An exclamation-point icon appears in the message box.
		/// </summary>
		MB_ICONWARNING = 0x000030,

		/// <summary>
		/// An exclamation-point icon appears in the message box.
		/// </summary>
		MB_ICONEXCLAMATION = 0x000030,

		/// <summary>
		/// An icon consisting of a lowercase letter i in a circle appears in the message box.
		/// </summary>
		MB_ICONASTERISK = 0x000040,

		/// <summary>
		/// An icon consisting of a lowercase letter i in a circle appears in the message box.
		/// </summary>
		MB_ICONINFORMATION = 0x00000040,

		/// <summary>
		/// Uses an user defined icon
		/// </summary>
		MB_USERICON = 0x000080,

		/// <summary>
		/// The first button is the default button.
		/// MB_DEFBUTTON1 is the default unless <see cref="MB_DEFBUTTON2"/>, <see cref="MB_DEFBUTTON3"/>, or <see cref="MB_DEFBUTTON4"/> is specified.
		/// </summary>
		MB_DEFBUTTON1 = 0x000000,

		/// <summary>
		/// The second button is the default button.
		/// </summary>
		MB_DEFBUTTON2 = 0x000100,

		/// <summary>
		/// The third button is the default button.
		/// </summary>
		MB_DEFBUTTON3 = 0x000200,

		/// <summary>
		/// The fourth button is the default button.
		/// </summary>
		MB_DEFBUTTON4 = 0x000300,

		/// <summary>
		/// The user must respond to the message box before continuing work in the window identified by the hWnd parameter.
		/// However, the user can move to the windows of other threads and work in those windows.
		/// Depending on the hierarchy of windows in the application, the user may be able to move to other windows within the thread.
		/// All child windows of the parent of the message box are automatically disabled, but pop-up windows are not.
		/// MB_APPLMODAL is the default if neither <see cref="MB_SYSTEMMODAL"/> nor <see cref="MB_TASKMODAL"/> is specified.
		/// </summary>
		MB_APPLMODAL = 0x000000,

		/// <summary>
		/// Same as <see cref="MB_APPLMODAL"/> except that the message box has the WS_EX_TOPMOST style.
		/// Use system-modal message boxes to notify the user of serious, potentially damaging errors that require immediate attention (for example, running out of memory).
		/// This flag has no effect on the user's ability to interact with windows other than those associated with hWnd.
		/// </summary>
		MB_SYSTEMMODAL = 0x001000,

		/// <summary>
		/// Same as <see cref="MB_APPLMODAL"/> except that all the top-level windows belonging to the current thread are disabled if the hWnd parameter is NULL.
		/// Use this flag when the calling application or library does not have a window handle available but still needs to prevent input to other windows
		/// in the calling thread without suspending other threads.
		/// </summary>
		MB_TASKMODAL = 0x002000,

		/// <summary>
		/// Adds a Help button to the message box. When the user clicks the Help button or presses F1, the system sends a <see cref="WindowMessage.WM_HELP"/> message to the owner.
		/// </summary>
		MB_HELP = 0x004000,

		/// <summary>
		/// Undocumented
		/// </summary>
		MB_NOFOCUS = 0x008000,

		/// <summary>
		/// The message box becomes the foreground window. Internally, the system calls the <see cref="SetForegroundWindow"/> function for the message box.
		/// </summary>
		MB_SETFOREGROUND = 0x00010000,

		/// <summary>
		/// Same as desktop of the interactive window station. For more information, see Window Stations.
		/// If the current input desktop is not the default desktop, MessageBox does not return until the user switches to the default desktop.
		/// </summary>
		MB_DEFAULT_DESKTOP_ONLY = 0x00020000,

		/// <summary>
		/// The message box is created with the WS_EX_TOPMOST window style.
		/// </summary>
		MB_TOPMOST = 0x00040000,

		/// <summary>
		/// The text is right-justified.
		/// </summary>
		MB_RIGHT = 0x00080000,

		/// <summary>
		/// Displays message and caption text using right-to-left reading order on Hebrew and Arabic systems.
		/// </summary>
		MB_RTLREADING = 0x00100000,

		/// <summary>
		/// The caller is a service notifying the user of an event. The function displays a message box on the current active desktop, even if there is no user logged on to the computer.
		/// If this flag is set, the hWnd parameter must be NULL. This is so that the message box can appear on a desktop other than the desktop corresponding to the hWnd.
		/// </summary>
		/// <remarks>Terminal Services: If the calling thread has an impersonation token, the function directs the message box to the session specified in the impersonation token.</remarks>
		MB_SERVICE_NOTIFICATION = 0x00200000,
	}
}