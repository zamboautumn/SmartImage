﻿using System.Net.NetworkInformation;
using Flurl.Http;
using JetBrains.Annotations;

namespace SmartImage.Lib.Engines;

public interface IEndpoint : IDisposable
{
	public string EndpointUrl { get; }

	/*[NotNull]
	public Task<IFlurlResponse> GetEndpointResponseAsync(TimeSpan fs)
	{
		return EndpointUrl.WithTimeout(fs)
			.OnError(rx =>
			{
				rx.ExceptionHandled = true;
			})
			.AllowAnyHttpStatus()
			.WithAutoRedirect(true)
			.GetAsync();

	}*/
}