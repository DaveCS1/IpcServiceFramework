﻿using Castle.DynamicProxy;
using JKang.IpcServiceFramework.IO;
using JKang.IpcServiceFramework.Services;
using System;
using System.IO.Pipes;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace JKang.IpcServiceFramework
{
    public class IpcServiceClient<TInterface>
        where TInterface : class
    {
        private readonly string _pipeName;
        private readonly IIpcMessageSerializer _serializer;
        private readonly IValueConverter _converter;

        public IpcServiceClient(string pipeName)
            : this(pipeName, new DefaultIpcMessageSerializer(), new DefaultValueConverter())
        { }

        internal IpcServiceClient(string pipeName,
            IIpcMessageSerializer serializer,
            IValueConverter converter)
        {
            _pipeName = pipeName;
            _serializer = serializer;
            _converter = converter;
        }

        public async Task InvokeAsync(Expression<Action<TInterface>> exp)
        {
            IpcRequest request = GetRequest(exp, new MyInterceptor());
            IpcResponse response = await GetResponseAsync(request);

            if (response.Succeed)
            {
                return;
            }
            else
            {
                throw new InvalidOperationException(response.Failure);
            }
        }

        public async Task<TResult> InvokeAsync<TResult>(Expression<Func<TInterface, TResult>> exp)
        {
            IpcRequest request = GetRequest(exp, new MyInterceptor<TResult>());
            IpcResponse response = await GetResponseAsync(request);

            if (response.Succeed)
            {
                if (_converter.TryConvert(response.Data, typeof(TResult), out object @return))
                {
                    return (TResult)@return;
                }
                else
                {
                    throw new InvalidOperationException($"Unable to convert returned value to '{typeof(TResult).Name}'.");
                }
            }
            else
            {
                throw new InvalidOperationException(response.Failure);
            }
        }

        private static IpcRequest GetRequest(Expression exp, MyInterceptor interceptor)
        {
            if (!(exp is LambdaExpression lamdaExp))
            {
                throw new ArgumentException("Only support lamda expresion, ex: x => x.GetData(a, b)");
            }

            if (!(lamdaExp.Body is MethodCallExpression methodCallExp))
            {
                throw new ArgumentException("Only support calling method, ex: x => x.GetData(a, b)");
            }

            var proxyGenerator = new ProxyGenerator();
            TInterface proxy = proxyGenerator.CreateInterfaceProxyWithoutTarget<TInterface>(interceptor);
            Delegate @delegate = lamdaExp.Compile();
            @delegate.DynamicInvoke(proxy);

            return new IpcRequest
            {
                InterfaceName = typeof(TInterface).AssemblyQualifiedName,
                MethodName = interceptor.LastInvocation.Method.Name,
                Parameters = interceptor.LastInvocation.Arguments,
            };
        }

        private async Task<IpcResponse> GetResponseAsync(IpcRequest request)
        {
            using (var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.None))
            using (var writer = new IpcWriter(client, _serializer, leaveOpen: true))
            using (var reader = new IpcReader(client, _serializer, leaveOpen: true))
            {
                await client.ConnectAsync();

                // send request
                writer.Write(request);

                // receive response
                return reader.ReadIpcResponse();
            }
        }

        private class MyInterceptor : IInterceptor
        {
            public IInvocation LastInvocation { get; private set; }

            public virtual void Intercept(IInvocation invocation)
            {
                LastInvocation = invocation;
            }
        }

        private class MyInterceptor<TResult> : MyInterceptor
        {
            public override void Intercept(IInvocation invocation)
            {
                base.Intercept(invocation);
                invocation.ReturnValue = default(TResult);
            }
        }
    }
}
