﻿global using ReflectionHelper = Novus.Utilities.ReflectionHelper;
using JetBrains.Annotations;
using SmartImage.Lib.Engines;
using SmartImage.Lib.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kantan.Cli.Controls;
using Kantan.Diagnostics;
using Kantan.Model;
using Kantan.Net;
using Kantan.Text;
using Kantan.Utilities;
using Novus.Utilities;
using Novus.OS.Win32;
using SmartImage.Lib.Engines.Model;

#pragma warning disable IDE0066, CA1416

namespace SmartImage.Lib.Searching;

public enum ResultStatus
{
	/// <summary>
	/// Succeeded in parsing/retrieving result
	/// </summary>
	Success,

	/// <summary>
	/// No results found
	/// </summary>
	NoResults,

	/// <summary>
	/// Server unavailable
	/// </summary>
	Unavailable,

	/// <summary>
	/// Failed to parse/retrieve results
	/// </summary>
	Failure,

	/// <summary>
	/// Result is extraneous
	/// </summary>
	Extraneous,

	/// <summary>
	/// Engine which returned the result is on cooldown
	/// </summary>
	Cooldown
}

/// <summary>
/// Describes a search result
/// </summary>
public class SearchResult : IResult
{
	/// <summary>
	/// Primary image result
	/// </summary>
	public ImageResult PrimaryResult { get; internal set; }

	/// <summary>
	/// Other image results
	/// </summary>
	public List<ImageResult> OtherResults { get; internal set; }

	/// <summary>
	/// <see cref="OtherResults"/> &#x222A; <see cref="PrimaryResult"/>
	/// </summary>
	public List<ImageResult> AllResults => OtherResults.Union(new[] { PrimaryResult }).ToList();

	/// <summary>
	/// Undifferentiated URI
	/// </summary>
	public Uri RawUri { get; internal set; }

	/// <summary>
	/// The <see cref="BaseSearchEngine"/> that returned this result
	/// </summary>
	public BaseSearchEngine Engine { get; internal init; }

	/// <summary>
	/// Result status
	/// </summary>
	public ResultStatus Status { get; internal set; }

	/// <summary>
	/// Error message; if applicable
	/// </summary>
	[CanBeNull]
	public string ErrorMessage { get; internal set; }

	/// <summary>
	/// Indicates whether this result is detailed.
	/// <para></para>
	/// If filtering is enabled (i.e., <see cref="SearchConfig.Filtering"/> is <c>true</c>), this determines whether the
	/// result is filtered.
	/// </summary>
	public bool IsNonPrimitive => (Status != ResultStatus.Extraneous && PrimaryResult.Url != null);

	public SearchResultOrigin Origin { get; internal set; }

	public bool IsSuccessful
	{
		get
		{
			switch (Status) {
				case ResultStatus.Failure:
				case ResultStatus.Unavailable:
					return false;

				case ResultStatus.Success:
				case ResultStatus.NoResults:
				case ResultStatus.Cooldown:
				case ResultStatus.Extraneous:
					return true;

				default:
					throw new ArgumentOutOfRangeException();
			}
		}
	}

	public SearchResult(BaseSearchEngine engine)
	{
		Engine = engine;

		PrimaryResult = new ImageResult();
		OtherResults  = new List<ImageResult>();
	}


	public bool Scanned { get; internal set; }

	public async Task<List<ImageResult>> FindDirectResultsAsync()
	{
		Debug.WriteLine($"searching within {Engine.Name}");

		var directResults = new List<ImageResult>();

		foreach (ImageResult ir in AllResults) {
			var b = await ir.ScanForImagesAsync();

			if (b && !directResults.Contains(ir)) {

				Debug.WriteLine($"{nameof(SearchResult)}: Found direct result {ir.Direct.Url}");
				directResults.Add(ir);
				PrimaryResult.Direct.Url ??= ir.Direct.Url;
			}
		}

		Scanned = true;

		return directResults;
	}


	public ConsoleOption GetConsoleOption()
	{
		//todo

		// var color = Elements.EngineColorMap[result.Engine.EngineOption];
		const float factor = -.2f;

		// var of = Array.IndexOf(Enum.GetValues(typeof(SearchEngineOptions)), Engine.EngineOption);
		var of = (int) Engine.EngineOption;

		/*var random = new Random(of);

		var c = Color.FromArgb(random.Next(0, byte.MaxValue), random.Next(0, byte.MaxValue),
		                       random.Next(0, byte.MaxValue));*/


		var option = new ConsoleOption
		{

			Functions = new()
			{
				[ConsoleOption.NC_FN_MAIN] = IResult.CreateOpenFunction(PrimaryResult is { Url: { } }
					                                                        ? PrimaryResult.Url
					                                                        : RawUri),

				[ConsoleOption.NC_FN_SHIFT] = IResult.CreateOpenFunction(RawUri),

			},


			Name = Engine.Name,
			Data = this.Data,
		};

		option.Functions[ConsoleOption.NC_FN_ALT] = () =>
		{
			if (OtherResults.Any()) {
				//todo


				const string n = "Other result";

				int i = 0;

				var fallback = Color.AliceBlue;

				var options = OtherResults
				              .Select(r =>
				              {
					              var c = option.Color ?? fallback;

					              return r.GetConsoleOption($"{n} #{i++}", c.ChangeBrightness(factor));
				              })
				              .ToArray();

				var dialog = new ConsoleDialog
				{
					Options = options
				};

				dialog.ReadInput();
			}

			return null;
		};

		option.Functions[ConsoleOption.NC_FN_COMBO] =
			IResult.CreateDownloadFunction(() => PrimaryResult.Direct.Url);

		option.Functions[ConsoleOption.NC_FN_CTRL] = () =>
		{
			var cts = new CancellationTokenSource();

			if (OperatingSystem.IsWindows()) {
				ConsoleProgressIndicator.Start(cts);
			}

			_ = FindDirectResultsAsync();

			cts.Cancel();
			cts.Dispose();

			option.Data = Data;

			return null;
		};

		return option;
	}

	public Dictionary<string, object> Data
	{
		get
		{
			var map = new Dictionary<string, object>();

			// map.Add(nameof(PrimaryResult), PrimaryResult);

			foreach ((string key, object value) in PrimaryResult.Data) {
				map.Add(key, value);
			}

			map.Add("Raw", RawUri);

			if (OtherResults.Count != 0) {
				map.Add("Other image results", OtherResults.Count);
			}

			if (ErrorMessage != null) {
				map.Add("Error", ErrorMessage);
			}

			if (!IsSuccessful) {
				map.Add("Status", Status);
			}


			return map;
		}
	}

	public void Dispose()
	{
		foreach (ImageResult imageResult in AllResults) {
			imageResult.Dispose();
		}

		Origin.Dispose();
	}

	[ThreadStatic]
	internal static readonly Random Random = new();
}