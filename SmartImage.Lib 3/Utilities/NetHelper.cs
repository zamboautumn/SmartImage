﻿// Read Stanton SmartImage.Lib NodeHelper.cs
// 2023-01-13 @ 11:37 PM

using System.Diagnostics;
using AngleSharp.Dom;
using Flurl.Http;
using Flurl.Http.Configuration;

namespace SmartImage.Lib.Utilities;

internal static class NetHelper
{
	internal static INode TryFindSingleElementByClassName(this INodeList nodes, string className)
	{
		return nodes.FirstOrDefault(f => f is IElement e && e.ClassName == className);
	}
	
	internal static INode[] TryFindElementsByClassName(this INodeList nodes, string className)
	{
		return nodes.Where(f => f is IElement e && e.ClassName == className).ToArray();
	}

	public static long? GetContentLength(this IFlurlResponse r)
	{
		return r.Headers.TryGetFirst("Content-Length", out var cls) ? long.Parse(cls) : null;

	}

	public static IEnumerable<string> QueryAllAttribute(this IParentNode doc, string sel, string attr)
	{
		return doc.QuerySelectorAll(sel)
			.Select(e => e.GetAttribute(attr));
	}

	public static Action<FlurlHttpSettings> Configure()
	{
		return r =>
		{
			r.OnError = SearchClient.Client.Settings.OnError;
		};
	}
}