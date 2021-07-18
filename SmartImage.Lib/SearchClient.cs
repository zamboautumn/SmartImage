﻿using JetBrains.Annotations;
using Novus.Utilities;
using SmartImage.Lib.Engines;
using SmartImage.Lib.Searching;
using SmartImage.Lib.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kantan.Net;
using Kantan.Utilities;
using SmartImage.Lib.Upload;
using static Kantan.Diagnostics.LogCategories;

// ReSharper disable CognitiveComplexity

// ReSharper disable LoopCanBeConvertedToQuery

// ReSharper disable UnusedMember.Global

namespace SmartImage.Lib
{
	/// <summary>
	/// Handles searches
	/// </summary>
	public sealed class SearchClient
	{
		public SearchClient(SearchConfig config)
		{
			Config = config;

			Results = new List<SearchResult>();

			FilteredResults = new List<SearchResult>();

			Reload();
		}


		/// <summary>
		/// The configuration to use when searching
		/// </summary>
		public SearchConfig Config { get; init; }

		/// <summary>
		/// Whether the search process is complete
		/// </summary>
		public bool IsComplete { get; private set; }

		/// <summary>
		/// Search engines to use
		/// </summary>
		public BaseSearchEngine[] Engines { get; private set; }

		/// <summary>
		/// Contains search results
		/// </summary>
		public List<SearchResult> Results { get; }

		/// <summary>
		/// Contains filtered search results
		/// </summary>
		public List<SearchResult> FilteredResults { get; }

		/// <summary>
		/// Reloads <see cref="Config"/> and <see cref="Engines"/> accordingly.
		/// </summary>
		public void Reload()
		{
			if (Config.SearchEngines == SearchEngineOptions.None) {
				Config.SearchEngines = SearchEngineOptions.All;
			}

			Engines = GetAllSearchEngines()
			          .Where(e => Config.SearchEngines.HasFlag(e.EngineOption))
			          .ToArray();


			Trace.WriteLine($"Engines: {Config.SearchEngines}");
		}

		/// <summary>
		/// Resets this instance in order to perform a new search.
		/// </summary>
		public void Reset()
		{
			Results.Clear();
			IsComplete = false;
		}

		#region Primary operations

		/// <summary>
		/// Performs an image search asynchronously.
		/// </summary>
		public async Task RunSearchAsync()
		{
			if (IsComplete) {
				Reset();
			}

			var tasks = new List<Task<SearchResult>>(Engines.Select(e => e.GetResultAsync(Config.Query)));

			while (!IsComplete) {
				var finished = await Task.WhenAny(tasks);

				var value = await finished;

				tasks.Remove(finished);

				bool? isFiltered;
				bool  isPriority = Config.PriorityEngines.HasFlag(value.Engine.EngineOption);

				//
				//                          Filtering
				//                         /         \
				//                     true           false
				//                     /                 \
				//               IsNonPrimitive         [Results]
				//                /          \
				//              true         false
				//             /               \
				//        [Results]        [FilteredResults]
				//

				if (Config.Filtering) {

					if (value.IsNonPrimitive) {
						Results.Add(value);
						isFiltered = false;
					}
					else {
						FilteredResults.Add(value);
						isFiltered = true;
					}
				}
				else {
					Results.Add(value);
					isFiltered = null;
				}

				// Call event
				ResultCompleted?.Invoke(null, new SearchResultEventArgs(value)
				{
					IsFiltered = isFiltered,
					IsPriority = isPriority
				});


				IsComplete = !tasks.Any();
			}

			Trace.WriteLine($"{nameof(SearchClient)}: Search complete", C_SUCCESS);

			SearchCompleted?.Invoke(null, Results);

			if (Config.Notification) {


				var args = new ExtraResultEventArgs()
				{
					Results = this.Results,
					Best    = FindBestResult(),
				};

				if (Config.NotificationImage) {

					Debug.WriteLine($"Finding direct result");

					var direct = FindDirectResult();

					Debug.WriteLine(direct);

					Debug.WriteLine(direct.Direct.ToString());

					args.Direct = direct;
				}

				ExtraResultsCompleted?.Invoke(null, args);


				/*
				 * var direct = Program.Client.FindDirectResult();

				Debug.WriteLine(direct);

				Debug.WriteLine(direct.Direct.ToString());
				 */
			}

		}

		#endregion


		#region Secondary operations

		/// <summary>
		/// Refines search results by searching with the most-detailed result (<see cref="FindDirectResult"/>).
		/// </summary>
		public async Task RefineSearchAsync()
		{
			if (!IsComplete) {
				throw new SmartImageException(ERR_SEARCH_NOT_COMPLETE);
			}

			Debug.WriteLine("Finding best result");

			var best = FindDirectResult();

			if (best == null) {
				throw new SmartImageException(ERR_NO_BEST_RESULT);
			}

			var uri = best.Direct;

			Debug.WriteLine($"Refining by {uri}");

			Config.Query = uri;

			await RunSearchAsync();
		}

		/// <summary>
		/// Maximizes search results by using the specified property selector.
		/// </summary>
		/// <returns><see cref="Results"/> ordered by <paramref name="property"/></returns>
		public List<SearchResult> MaximizeResults<T>(Func<SearchResult, T> property)
		{
			if (!IsComplete) {
				throw new SmartImageException(ERR_SEARCH_NOT_COMPLETE);
			}

			var res = Results.OrderByDescending(property).ToList();

			res.RemoveAll(r => !r.IsNonPrimitive);

			return res;
		}


		[CanBeNull]
		public ImageResult FindDirectResult() => FindDirectResults().FirstOrDefault();

		public ImageResult[] FindDirectResults(int count = 5)
		{
			var best = FindBestResults().ToList();

			var options = new ParallelOptions()
			{
				MaxDegreeOfParallelism = Int32.MaxValue,
				TaskScheduler          = TaskScheduler.Default,
			};

			Debug.WriteLine($"Found {best.Count} best results");

			var images = new ConcurrentBag<ImageResult>();

			Parallel.For(0, best.Count, options, (i, s) =>
			{
				//if (images.Count >= count) {
				//	s.Stop();
				//	return;
				//}

				//if (s.IsStopped) {
				//	return;
				//}

				var item = best[i];

				item.FindDirectImages();

				if (item.Direct == null) {
					return;
				}

				if (ImageHelper.IsDirect(item.Direct.ToString(), DirectImageType.Binary)) {
					//if (images.Count >= count) {
					//	s.Stop();
					//	return;
					//}

					Debug.WriteLine($"Adding {item.Direct}");

					images.Add(item);
					
				}
			});

			Debug.WriteLine($"Found {images.Count} direct results");

			return images.OrderByDescending(r => r.Similarity)
			             .ToArray();
		}

		[CanBeNull]
		public ImageResult FindBestResult() => FindBestResults().FirstOrDefault();

		/// <summary>
		/// Selects the most detailed results.
		/// </summary>
		/// <returns>The <see cref="ImageResult"/>s of the best <see cref="Results"/></returns>
		public ImageResult[] FindBestResults()
		{
			var best = Results.Where(r => r.IsNonPrimitive)
			                  .SelectMany(r =>
			                  {
				                  var x = r.OtherResults;
				                  x.Insert(0, r.PrimaryResult);
				                  return x;
			                  });

			//==================================================================//


			best = best
			       .AsParallel()
			       .OrderByDescending(r => r.Similarity)
			       .ThenByDescending(r => r.PixelResolution)
			       .ThenByDescending(r => r.DetailScore);


			return best.ToArray();
		}

		#endregion

		public static BaseUploadEngine[] GetAllUploadEngines()
		{
			return typeof(BaseUploadEngine).GetAllSubclasses()
			                               .Select(Activator.CreateInstance)
			                               .Cast<BaseUploadEngine>()
			                               .ToArray();
		}


		public static BaseSearchEngine[] GetAllSearchEngines()
		{
			return typeof(BaseSearchEngine).GetAllSubclasses()
			                               .Select(Activator.CreateInstance)
			                               .Cast<BaseSearchEngine>()
			                               .ToArray();
		}

		/// <summary>
		/// An event that fires whenever a result is returned (<see cref="RunSearchAsync"/>).
		/// </summary>
		public event EventHandler<SearchResultEventArgs> ResultCompleted;

		/// <summary>
		/// An event that fires when a search is complete (<see cref="RunSearchAsync"/>).
		/// </summary>
		public event EventHandler<List<SearchResult>> SearchCompleted;


		public event EventHandler<ExtraResultEventArgs> ExtraResultsCompleted;


		private const string ERR_SEARCH_NOT_COMPLETE = "Search must be completed";

		private const string ERR_NO_BEST_RESULT = "Could not find best result";
	}

	public sealed class ExtraResultEventArgs : EventArgs
	{
		public List<SearchResult> Results { get; init; }

		[CanBeNull]
		public ImageResult Direct { get; internal set; }

		public ImageResult Best { get; internal set; }
	}

	public sealed class SearchResultEventArgs : EventArgs
	{
		/// <summary>
		/// Search result
		/// </summary>
		public SearchResult Result { get; }

		/// <summary>
		/// When <see cref="SearchConfig.Filtering"/> is <c>true</c>:
		/// <c>true</c> if the result was filtered; <c>false</c> otherwise
		/// <para></para>
		/// When <see cref="SearchConfig.Filtering"/> is <c>false</c>:
		/// <c>null</c>
		/// </summary>
		public bool? IsFiltered { get; init; }

		/// <summary>
		/// Whether this result was returned by an engine that is
		/// one of the specified <see cref="SearchConfig.PriorityEngines"/>
		/// </summary>
		public bool IsPriority { get; init; }

		public SearchResultEventArgs(SearchResult result)
		{
			Result = result;
		}
	}
}