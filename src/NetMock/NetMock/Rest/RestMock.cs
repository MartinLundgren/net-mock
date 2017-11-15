﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using NetMock.Exceptions;
using NetMock.Server;
using NetMock.Utils;
using Newtonsoft.Json;

namespace NetMock.Rest
{
	public partial class RestMock : INetMock
	{
		private readonly HttpListenerController _httpListener;
		private readonly List<RestRequestSetup> _requestDefinitions;
		private readonly List<ReceivedRequest> _receivedRequests;
		private bool _isActivated;

		internal RestMock(string basePath, int port, Scheme scheme, X509Certificate2 certificate = null)
		{
			_httpListener = new HttpListenerController(HandleRequest, certificate);
			_requestDefinitions = new List<RestRequestSetup>();
			_receivedRequests = new List<ReceivedRequest>();
			_isActivated = false;

			BasePath = basePath;
			Port = port;
			Scheme = scheme;
			Certificate = certificate;
		}

		public string BasePath { get; }
		public int Port { get; }
		public Scheme Scheme { get; }
		public X509Certificate2 Certificate { get; }
		public HttpStatusCode DefaultResponseStatusCode { get; set; } = HttpStatusCode.NotImplemented;
		public UndefinedHandling UndefinedQueryParameterHandling { get; set; } = UndefinedHandling.Fail;
		public UndefinedHandling UndefinedHeaderHandling { get; set; } = UndefinedHandling.Ignore;
		public bool InterpretBodyAsJson { get; set; } = true;

		internal Uri BaseUri => new UriBuilder(Scheme == Scheme.Http ? Uri.UriSchemeHttp : Uri.UriSchemeHttps, "localhost", Port, BasePath).Uri;

		public void Activate()
		{
			if (_isActivated)
				throw new InvalidOperationException("Already activated");

			ParseRequestDefinitions();

			_httpListener.StartListening(BaseUri, useWildcardHost: true);

			_isActivated = true;
		}

		public void TearDown()
		{
			if (!_isActivated)
				return;

			_isActivated = false;

			_httpListener.StopListening();

			_receivedRequests.Clear();
			_requestDefinitions.Clear();
		}

		private void ParseRequestDefinitions()
		{
			_requestDefinitions.ForEach(requestDefinition => requestDefinition.Parse());
		}

		private string HandleRequest(HttpListenerRequest httpRequest)
		{
			try
			{
				ReceivedRequest request = _receivedRequests.AddAndReturn(new ReceivedRequest(httpRequest));

				var matchedRequestDefinition = _requestDefinitions
					.Select(requestDefinition => new
						{
							RequestDefinition = requestDefinition,
							IsMatch = requestDefinition.Match(request, out IList<MatchResult> matchResult),
							MatchResult = matchResult
						})
					.LastOrDefault(x => x.IsMatch);

				if (matchedRequestDefinition != null)
				{
					// todo: handle scenario when .Response is null (no Returns method has been called)
					object body = matchedRequestDefinition.RequestDefinition.Response.Body;
					return body is string bodyStr ? bodyStr : JsonConvert.SerializeObject(body);
				}
			}
			catch (Exception ex)
			{
				throw new StatusCodeException(HttpStatusCode.InternalServerError, "Unhandled exception", ex);
			}

			throw new StatusCodeException(DefaultResponseStatusCode);
		}
	}
}
