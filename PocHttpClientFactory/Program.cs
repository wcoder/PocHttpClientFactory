using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Refit;

namespace PocHttpClientFactory
{
    class Program
    {
        static async Task Main(string[] args)
        {
            
            var container = new RemoteServiceContainer()
                .Register<IApiService>("https://jsonplaceholder.typicode.com")
                .Register<IApiService2>("https://jsonplaceholder.typicode.com");
            
            var factory = new RemoteServiceFactory(container);
            
            var apiService = factory.CreateService<IApiService>();
            
            var apiService2 = factory.CreateService<IApiService2>();

            try
            {
                for (int i = 0; i < 1; i++)
                {
                    var data = await apiService.GetAllPhotosAsync(CancellationToken.None);
                    Console.WriteLine($"Done: {i}");
                    
                    var data2 = await apiService2.GetAllPhotosAsync(CancellationToken.None);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
                            
            Console.WriteLine("Hello World!");
        }
    }

    public class RemoteServiceFactory : IRemoteServiceFactory
    {
        private readonly IRemoteServiceContainer _container;

        public RemoteServiceFactory(IRemoteServiceContainer container)
        {
            _container = container;
        }
        
        public TService CreateService<TService>()
        {
            return _container.Get<TService>();
        }
    }
    
    public interface IRemoteServiceFactory
    {
        TService CreateService<TService>();
    }

    public class RemoteServiceContainer : IRemoteServiceContainer
    {
        private readonly IServiceCollection _services = new ServiceCollection();
        
        private IServiceProvider _provider;
        private bool _initialized;

        protected virtual void Initialize()
        {
            _services
                .AddTransient<AHandler>()
                .AddTransient<BHandler>();
            
            _services.AddLogging(builder =>
            {
                builder.AddProvider(new CustomLogProvider());
            });

            _initialized = true;
        }
        
        public IRemoteServiceContainer Register<TService>(string baseUrl) where TService : class
        {
            if (_initialized == false)
            {
                Initialize();
            }
            
            // https://docs.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/use-httpclientfactory-to-implement-resilient-http-requests

            _services
                .AddRefitClient<TService>()
                .ConfigureHttpClient(client => { client.BaseAddress = new Uri(baseUrl); })
                .ConfigurePrimaryHttpMessageHandler(() => // TODO YP: native handler here
                {
                    return new HttpClientHandler
                    {
                        AllowAutoRedirect = false,
                        UseDefaultCredentials = true
                    };
                })
                // This handler is on the outside and called first during the 
                // request, last during the response.
                .AddHttpMessageHandler<AHandler>()
                // This handler is on the inside, closest to the request being sent.
                .AddHttpMessageHandler<BHandler>()
                .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(600)))
                .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(10)));

            return this;
        }

        public TService Get<TService>()
        {
            if (_provider == null)
            {
                _provider = _services.BuildServiceProvider();
            }
            
            return _provider.GetService<TService>();
        }
    }

    public interface IRemoteServiceContainer
    {
        IRemoteServiceContainer Register<TService>(string baseUrl) where TService : class;

        TService Get<TService>();
    }
    
    public class AHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return base.SendAsync(request, cancellationToken);
        }
    }

    public class BHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return base.SendAsync(request, cancellationToken);
        }
    }

    public interface IApiService
    {
        [Get("/photos")]
        Task<IReadOnlyCollection<PhotoResponse>> GetAllPhotosAsync(CancellationToken cancellationToken);
    }
    
    public interface IApiService2
    {
        [Get("/photos")]
        Task<IReadOnlyCollection<PhotoResponse>> GetAllPhotosAsync(CancellationToken cancellationToken);
    }
    
    public class PhotoResponse
    {
        public int AlbumId { get; set; }
        public int Id { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public string ThumbnailUrl { get; set; }
    }

    public class CustomLogProvider : ILoggerProvider
    {
        public void Dispose()
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new CustomLogger();
        }
    }
    
    public class CustomLogger : ILogger
    {
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Debug.WriteLine(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }
    }
}