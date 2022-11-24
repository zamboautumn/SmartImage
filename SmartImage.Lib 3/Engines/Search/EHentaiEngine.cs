﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Flurl;
using Flurl.Http;
using Flurl.Http.Configuration;
using Flurl.Http.Content;
using Kantan.Net.Utilities;
using Kantan.Threading;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SmartImage.Lib.Engines.Search;

public sealed class EHentaiEngine : BaseSearchEngine, IParse<INode>
{
	private const string EHENTAI_INDEX_URI         = "https://forums.e-hentai.org/index.php";
	private const string EXHENTAI_IMAGE_LOOKUP_URI = "https://exhentai.org/upld/image_lookup.php";
	private const string EXHENTAI_URI              = "https://exhentai.org/";
	private const string EHENTAI_URI               = "https://e-hentai.org/";

	public EHentaiEngine() : base(EXHENTAI_URI)
	{
		Cookies = new CookieJar();
	}

	public CookieJar Cookies { get; }

	public override SearchEngineOptions EngineOption => default;

	public override void Dispose() { }

	/*
	 * https://gitlab.com/NekoInverter/EhViewer/-/tree/master/app/src/main/java/com/hippo/ehviewer/client
	 * https://gitlab.com/NekoInverter/EhViewer/-/tree/master/app/src/main/java/com/hippo/ehviewer
	 * https://gitlab.com/NekoInverter/EhViewer/-/blob/master/app/src/main/java/com/hippo/ehviewer/client/EhUrl.java
	 * https://gitlab.com/NekoInverter/EhViewer/-/blob/master/app/src/main/java/com/hippo/ehviewer/client/EhEngine.java
	 * https://gitlab.com/NekoInverter/EhViewer/-/blob/master/app/src/main/java/com/hippo/ehviewer/EhApplication.java
	 * https://gitlab.com/NekoInverter/EhViewer/-/blob/master/app/src/main/java/com/hippo/ehviewer/client/data/ListUrlBuilder.java
	 * https://gitlab.com/NekoInverter/EhViewer/-/blob/master/app/src/main/java/com/hippo/ehviewer/client/EhCookieStore.java
	 */

	public async Task<SearchResult> SearchImage(Stream s)
	{
		var sr = new SearchResult(this);

		var data = new MultipartFormDataContent()
		{
			{ new StreamContent(s), "sfile", "a.jpg" },
			{ new StringContent("fs_similar") },
			{ new StringContent("fs_covers") },
			{ new StringContent("fs_exp") },
			{ new StringContent("fs_sfile") }
		};

		// data.Add(new FileContent(f.FullName), "sfile", "a.jpg");

		/*var q = uri
		        .WithHeaders(new
		        {
			        User_Agent = HttpUtilities.UserAgent
		        }).WithAutoRedirect(true);

		FlurlHttp.Configure(settings =>
		{
			settings.Redirects.Enabled                    = true; // default true
			settings.Redirects.AllowSecureToInsecure      = true; // default false
			settings.Redirects.ForwardAuthorizationHeader = true; // default false
			settings.Redirects.MaxAutoRedirects           = 20;   // default 10 (consecutive)
		});

		q = cj.Aggregate(q, (current, kv) => current.WithCookie(kv.Key, kv.Value));
		
		// TODO: Flurl throws an exception because it detects "circular redirects"
		// https://github.com/tmenier/Flurl/issues/714

		var res   = await q.SendAsync(HttpMethod.Post, data);*/

		// var flurl = FlurlHttp.GlobalSettings.HttpClientFactory;
		// var mh   = flurl.CreateMessageHandler();
		// var cl    = flurl.CreateHttpClient(mh);

		using var clientHandler = new HttpClientHandler
		{
			AllowAutoRedirect              = true,
			MaxAutomaticRedirections       = 15,
			CheckCertificateRevocationList = false,
			UseCookies                     = true,
			CookieContainer                = new() { },
		};

		foreach (var c in Cookies) {
			clientHandler.CookieContainer.Add(new Cookie(c.Name, c.Value, c.Path, c.Domain));
		}

		var cl = new HttpClient(clientHandler) { };

		var req = new HttpRequestMessage(HttpMethod.Post, EXHENTAI_IMAGE_LOOKUP_URI)
		{
			Content = data,
			Headers =
			{
				{ "User-Agent", HttpUtilities.UserAgent }
			}
		};

		var res = await cl.SendAsync(req);

		var content = await res.Content.ReadAsStringAsync();

		return sr;
	}

	public async Task<IFlurlResponse> Auth()
	{
		var res = await EXHENTAI_URI.WithCookies(Cookies)
		                            .WithHeaders(new
		                            {
			                            User_Agent = HttpUtilities.UserAgent
		                            })
		                            .WithAutoRedirect(true)
		                            .GetAsync();
		return res;
	}

	public async Task<IFlurlResponse> Login(string username, string password)
	{
		var content = new MultipartFormDataContent()
		{
			{ new StringContent("1"), "CookieDate" },
			{ new StringContent("d"), "b" },
			{ new StringContent("1-6"), "bt" },
			{ new StringContent(username), "UserName" },
			{ new StringContent(password), "PassWord" },
			{ new StringContent("Login!"), "ipb_login_submit" }
		};

		var response = await EHENTAI_INDEX_URI
		                     .SetQueryParams(new
		                     {
			                     act  = "Login",
			                     CODE = 01
		                     }).WithHeaders(new
		                     {
			                     User_Agent = HttpUtilities.UserAgent
		                     })
		                     .WithCookies(out var cj)
		                     .PostAsync(content);

		foreach (FlurlCookie fc in cj) {
			Cookies.AddOrReplace(fc);
		}

		return response;
	}

	#region Overrides of WebContentSearchEngine

	public async Task<IEnumerable<INode>> GetItems(IDocument d)
	{
		return d.QuerySelectorAll(NodesSelector);
	}

	#region Implementation of IParse<INode>

	public string NodesSelector => ".gl1t";

	#endregion

	#endregion

	#region Implementation of IParse<INode>

	public async Task<SearchResultItem> ParseResultItemAsync(INode n, SearchResult r)
	{
		var e    =n.FirstChild as IHtmlElement;
		var attr =e.GetAttribute("a");
		var t    = e.FirstChild.TextContent;

		return new SearchResultItem(r)
		{
			Title = t,
			Url = attr
		};
	}

	#endregion

}