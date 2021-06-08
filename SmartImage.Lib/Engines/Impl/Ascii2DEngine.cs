﻿using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.XPath;
using SimpleCore.Net;
using SmartImage.Lib.Searching;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartImage.Lib.Engines.Impl
{
	public sealed class Ascii2DEngine : InterpretedSearchEngine
	{
		public Ascii2DEngine() : base("https://ascii2d.net/search/url/") { }

		public override SearchEngineOptions Engine => SearchEngineOptions.Ascii2D;

		public override string Name => Engine.ToString();

		/*
		 *
		 *
		 * color https://ascii2d.net/search/color/<hash>
		 *
		 * detail https://ascii2d.net/search/bovw/<hash>
		 *
		 */

		protected override IDocument GetDocument(SearchResult sr)
		{
			var url = sr.RawUri.ToString();

			var res = Network.GetSimpleResponse(url);

			// Get redirect url (color url)
			var newUrl = res.ResponseUri.ToString();

			// https://ascii2d.net/search/color/<hash>

			// Convert to detail url

			var detailUrl = newUrl.Replace("/color/", "/bovw/");
			
			sr.RawUri = new Uri(detailUrl);

			return base.GetDocument(sr);
		}
		
		protected override SearchResult Process(IDocument doc, SearchResult sr)
		{
			var nodes = doc.Body.SelectNodes("//*[contains(@class, 'info-box')]");

			var rg = new List<ImageResult>();

			foreach (var node in nodes) {
				var ir = new ImageResult();

				var info = node.ChildNodes.Where(n => !String.IsNullOrWhiteSpace(n.TextContent)).ToArray();

				var hash = info.First().TextContent;

				ir.OtherMetadata.Add("Hash", hash);

				var data = info[1].TextContent.Split(' ');

				var res = data[0].Split('x');
				ir.Width  = Int32.Parse(res[0]);
				ir.Height = Int32.Parse(res[1]);

				var fmt = data[1];

				var size = data[2];

				if (info.Length >= 3) {
					var node2 = info[2];
					var desc  = info.Last().FirstChild;
					var ns    = desc.NextSibling;

					if (node2.ChildNodes.Length >= 2 && node2.ChildNodes[1].ChildNodes.Length >= 2) {
						var node2Sub = node2.ChildNodes[1];

						if (node2Sub.ChildNodes.Length >= 8) {
							ir.Description = node2Sub.ChildNodes[3].TextContent.Trim();
							ir.Artist      = node2Sub.ChildNodes[5].TextContent.Trim();
							ir.Site        = node2Sub.ChildNodes[7].TextContent.Trim();
						}
					}

					if (ns.ChildNodes.Length >= 4) {
						var childNode = ns.ChildNodes[3];

						var l1 = ((IHtmlAnchorElement) childNode).GetAttribute("href");

						if (l1 is not null) {
							ir.Url = new Uri(l1);
						}
					}
				}

				rg.Add(ir);
			}

			// Skip original image

			rg = rg.Skip(1).ToList();

			sr.PrimaryResult = rg.First();

			//sr.PrimaryResult.UpdateFrom(rg[0]);

			sr.OtherResults.AddRange(rg);

			return sr;
		}
	}
}