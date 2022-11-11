﻿global using MN = System.Diagnostics.CodeAnalysis.MaybeNullAttribute;
global using CBN = JetBrains.Annotations.CanBeNullAttribute;
global using NN = System.Diagnostics.CodeAnalysis.NotNullAttribute;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Flurl.Http;
using Kantan.Net.Utilities;
using Novus.FileTypes;
using Kantan.Text;
using SmartImage.Lib.Engines.Upload;

[assembly: InternalsVisibleTo("SmartImage")]
namespace SmartImage.Lib;

//todo: UniFile

public sealed class SearchQuery : IDisposable
{
	public string Value { get; }

	public Stream Stream { get; }

	[MN]
	public Url Upload { get; private set; }

	public bool IsUrl { get; private init; }

	public bool IsFile { get; private init; }

	public FileType[] FileTypes { get; private init; }

	public long Size { get; private set; }

	internal SearchQuery(string value, Stream stream)
	{
		Value  = value;
		Stream = stream;
	}

	public static readonly SearchQuery Null = new(null, Stream.Null);

	public static async Task<SearchQuery> TryCreateAsync(string value)
	{
		// TODO: null or throw exception?

		value = value.CleanString();

		bool isFile, isUrl;
		var  stream = Stream.Null;

		isFile = File.Exists(value);

		if (isFile) {
			stream = File.OpenRead(value);
			isUrl  = false;
		}
		else {
			try {
				var res = await value.AllowAnyHttpStatus().WithHeaders(new
				                     {
										 User_Agent=HttpUtilities.UserAgent
				                     })
				                     .GetAsync();

				/*if (!res.ResponseMessage.IsSuccessStatusCode) {
					Debug.WriteLine($"invalid status code {res.ResponseMessage.StatusCode} {value}");
					return null;
				}*/

				stream = await res.GetStreamAsync();
				isUrl  = true;

			}
			catch (FlurlHttpException e) {
				Debug.WriteLine($"{e.Message} ({value})", nameof(TryCreateAsync));
				// return await Task.FromException<SearchQuery>(e);
				return null;
			}
		}

		// Trace.Assert((isFile || isUrl) && !(isFile && isUrl));

		var types = (await IFileTypeResolver.Default.ResolveAsync(stream)).ToArray();

		if (!types.Any(t => t.IsType(FileType.MT_IMAGE))) {
			// var e = new ArgumentException("Invalid file types", nameof(value));
			// return await Task.FromException<SearchQuery>(e);
			Debug.WriteLine($"Invalid file types: {value} {types.QuickJoin()}", nameof(TryCreateAsync));
			return null;

		}

		var sq = new SearchQuery(value, stream)
		{
			IsFile    = isFile,
			IsUrl     = isUrl,
			FileTypes = types
		};

		return sq;
	}

	public async Task<Url> UploadAsync(BaseUploadEngine engine = null)
	{
		if (IsUrl) {
			Upload = Value;
			Size   = -1; // todo: indeterminate/unknown size
			Debug.WriteLine($"Skipping upload for {Value}", nameof(UploadAsync));
		}
		else {
			engine ??= BaseUploadEngine.Default;
			var u = await engine.UploadFileAsync(Value);
			Upload = u;
			Size   = engine.Size;
		}

		return Upload;
	}

	#region IDisposable

	public void Dispose()
	{
		Stream.Dispose();
	}

	#endregion

	#region Overrides of Object

	public override string ToString()
	{
		var s = IsFile ? "File" : (IsUrl ? "Url" : null);
		return $"{Value} ({s}) : {Upload} [{FileTypes.QuickJoin()}]";
	}

	#endregion

	public static bool IsIndicatorValid(string str)
	{
		return Url.IsValid(str) || File.Exists(str);
	}
}