using System;
using System.Collections.Generic;
using SmartImage.Engines;
using SmartImage.Engines.SauceNao;
using SmartImage.Engines.TraceMoe;
using SmartImage.Model;
using SmartImage.Utilities;

namespace SmartImage
{
	public static class Search
	{
		public static readonly ISearchEngine[] AllEngines =
		{
			new SauceNao(),
			new ImgOps(),
			new GoogleImages(),
			new TinEye(),
			new Iqdb(),
			new TraceMoe(),
			new KarmaDecay(),
		};


		public static SearchResult[] RunSearches(string imgUrl, SearchEngines engines)
		{
			var list = new List<SearchResult>
			{
				new SearchResult(imgUrl, "(Original image)")
			};


			foreach (var idx in AllEngines) {
				if (engines.HasFlag(idx.Engine)) {
					string wait = string.Format("{0}: ...", idx.Engine);

					Cli.WithColor(ConsoleColor.Blue, () =>
					{
						//
						Console.Write(wait);
					});


					// Run search
					var result = idx.GetResult(imgUrl);

					if (result != null) {
						
						string clear = new string('\b', wait.Length);
						Console.Write(clear);

						var url = result.Url;

						if (url != null) {
							Cli.WithColor(ConsoleColor.Green, () =>
							{
								//
								Console.Write("{0}: Done\n", result.Name);
							});

							if (Config.PriorityEngines.HasFlag(idx.Engine)) {
								Common.OpenUrl(result.Url);
							}
						}
						else {
							Cli.WithColor(ConsoleColor.Yellow, () =>
							{
								//
								Console.Write("{0}: Done (url is null!)\n", result.Name);
							});
						}

						list.Add(result);
					}
					else { }
				}
			}


			return list.ToArray();
		}
	}
}