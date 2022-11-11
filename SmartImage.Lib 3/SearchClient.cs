﻿global using ICBN = JetBrains.Annotations.ItemCanBeNullAttribute;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AngleSharp.Html;
using Flurl.Http;
using Kantan.Net.Utilities;
using Microsoft.Extensions.Configuration;
using Novus.FileTypes;
using SmartImage.Lib.Engines;

namespace SmartImage.Lib;

public sealed class SearchClient : IDisposable
{
	public SearchConfig Config { get; init; }

	public bool IsComplete { get; private set; }

	public BaseSearchEngine[] Engines { get; private set; }

	public SearchClient(SearchConfig cfg)
	{
		Config  = cfg;
		Engines = Array.Empty<BaseSearchEngine>();
	}

	static SearchClient() { }

	public delegate void ResultCompleteCallback(object sender, SearchResult e);

	public delegate void SearchCompleteCallback(object sender, List<SearchResult> e);

	public ResultCompleteCallback OnResult { get; set; }

	public SearchCompleteCallback OnComplete { get; set; }

	/// <summary>
	/// Runs a search of <paramref name="query"/>.
	/// </summary>
	/// <param name="query">Search query</param>
	/// <param name="token">Cancellation token passed to <see cref="BaseSearchEngine.GetResultAsync"/></param>
	public async Task<List<SearchResult>> RunSearchAsync(SearchQuery query, CancellationToken? token = null)
	{
		token ??= CancellationToken.None;

		Engines = BaseSearchEngine.All.Where(e => Config.SearchEngines.HasFlag(e.EngineOption)).ToArray();

		var tasks = Engines.Select(e => e.GetResultAsync(query, token))
		                   .ToList();

		var results = new List<SearchResult>();

		while (tasks.Any()) {
			if (token.Value.IsCancellationRequested) {

				Debug.WriteLine($"Cancellation requested", nameof(RunSearchAsync));
				IsComplete = true;
				return results;
			}

			var task   = await Task.WhenAny(tasks);
			var result = await task;

			OnResult?.Invoke(this, result);

			if (Config.PriorityEngines.HasFlag(result.Engine.EngineOption)) {

				var url1 = result.Best?.Url ?? result.RawUrl;

				HttpUtilities.TryOpenUrl(url1);
			}

			results.Add(result);
			tasks.Remove(task);
		}

		OnComplete?.Invoke(this, results);

		IsComplete = true;

		if (Config.PriorityEngines == SearchEngineOptions.Auto) {

			try {
				var result = Optimize(results).First();

				Debug.WriteLine($"Auto: {result}", nameof(RunSearchAsync));

				HttpUtilities.TryOpenUrl(result.Url);
			}
			catch (Exception e) { }
		}

		return results;
	}

	public static IEnumerable<SearchResultItem> Optimize(List<SearchResult> results)
	{
		return results.SelectMany(r => r.Results)
		              .Where(r =>
		              {
			              var (isFile, isUri) = UniFile.IsUriOrFile(r.Url);
			              return isFile || isUri;
		              })
		              .OrderByDescending(r => r.Score)
		              .ThenByDescending(r => r.Similarity);
	}

	public static async Task<List<UniFile>> GetDirectImages(List<SearchResult> results)
	{
		var filter = Optimize(results).AsParallel()
		                         .DistinctBy(r => r.Url)
		                         .Where(r => r.Score >= SearchResultItem.SCORE_THRESHOLD) // probably can be removed/reduced
		                         .Select(r =>
		                         {
			                         return r.GetUniAsync();
		                         }).ToList();

		var di = new List<UniFile>();

		while (filter.Any()) {
			var t1 = await Task.WhenAny(filter);
			filter.Remove(t1);
			var uf = await t1;

			if (uf != null) {
				di.Add(uf);
			}
		}

		return di;
	}

	#region Implementation of IDisposable

	public void Dispose()
	{
		foreach (var engine in Engines) {
			engine.Dispose();
		}
	}

	#endregion
}