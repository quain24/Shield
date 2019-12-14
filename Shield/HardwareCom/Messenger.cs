﻿using Shield.CommonInterfaces;
using Shield.Enums;
using Shield.HardwareCom.Factories;
using Shield.HardwareCom.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Shield.HardwareCom
{
    /// <summary>
    /// Basic communication class - every command sent or received goes through this class.
    /// Constantly monitors incoming communication and simultaneously sends given commands.
    /// Needs to be setup by <c>Setup</c> method.
    /// OPEN / START RECEIVING / START DECODING.
    /// Returns decoded commands by <c>CommandReceived</c> event.
    /// </summary>
    public class Messenger : IMessanger
    {
        private ICommunicationDeviceFactory _communicationDeviceFactory;
        private ICommunicationDevice _device;
        private ICommandTranslator _commandTranslator;
        private IIncomingDataPreparer _incomingDataPreparer;

        private CancellationTokenSource _receiveCTS = new CancellationTokenSource();
        private CancellationTokenSource _decodingCTS = new CancellationTokenSource();

        private BlockingCollection<string> _rawDataBuffer = new BlockingCollection<string>();

        private bool _setupSuccessufl = false;
        private bool _disposed = false;
        private bool _decoderRunning = false;
        private bool _receiverRunning = false;
        private bool _isSending = false;

        private object _decoderLock = new object();
        private object _receiverLock = new object();

        public event EventHandler<ICommandModel> CommandReceived;

        public bool IsOpen { get => _device.IsOpen; }
        public bool IsReceiving { get => _receiverRunning; }
        public bool IsDecoding { get => _decoderRunning; }
        public bool IsSending { get => _isSending; }

        public Messenger(ICommunicationDeviceFactory communicationDeviceFactory, ICommandTranslator commandTranslator, IIncomingDataPreparer incomingDataPreparer)
        {
            _communicationDeviceFactory = communicationDeviceFactory;
            _commandTranslator = commandTranslator;
            _incomingDataPreparer = incomingDataPreparer;
        }

        public bool Setup(DeviceType type)
        {
            _device = _communicationDeviceFactory.Device(type);
            if (_device is null)
                return _setupSuccessufl = false;

            return _setupSuccessufl = true;
        }

        public void Open()
        {
            if (_setupSuccessufl && IsOpen == false)
                _device.Open();
        }

        public void Close()
        {
            StopDecoding();
            StopReceiving();
            _rawDataBuffer.Dispose();
            _rawDataBuffer = new BlockingCollection<string>();
            _device?.Close();
        }

        public async Task StartReceiveAsync(CancellationToken ct)
        {
            if (!_receiverRunning)
            {
                lock (_receiverLock)
                {
                    if (_receiverRunning || !IsOpen || !_setupSuccessufl)
                        return;
                    _receiverRunning = true;
                }
            }

            CancellationToken internalCT = ct == default ? _receiveCTS.Token : ct;
            if (internalCT == ct)
                internalCT.Register(StopReceiving);

            try
            {
                while (_device.IsOpen)
                {
                    internalCT.ThrowIfCancellationRequested();
                    string toAdd = await _device.ReceiveAsync(internalCT).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(toAdd))
                    {
                        internalCT.ThrowIfCancellationRequested();
                        _rawDataBuffer.Add(toAdd);
                    }
                }
                _receiverRunning = false;
            }
            catch (OperationCanceledException)
            {
                _receiverRunning = false;
            }
        }

        public async Task StartDecodingAsync(CancellationToken ct)
        {
            if (!_decoderRunning)
            {
                lock (_decoderLock)
                {
                    if (!_decoderRunning)
                        _decoderRunning = true;
                    else
                        return;
                }
            }

            CancellationToken internalCT = ct == default ? _decodingCTS.Token : ct;
            if (internalCT == ct)
                internalCT.Register(StopDecoding);

            string workpiece = string.Empty;

            try
            {
                while (true && _rawDataBuffer != null)
                {
                    internalCT.ThrowIfCancellationRequested();
                    if (_rawDataBuffer.TryTake(out workpiece, 50))
                    {
                        List<string> output = _incomingDataPreparer.DataSearch(workpiece);
                        if (output is null || output.Count == 0)
                            continue;
                        foreach (string s in output)
                        {
                            internalCT.ThrowIfCancellationRequested();
                            ICommandModel receivedCommad = _commandTranslator.FromString(s);
                            OnCommandReceived(receivedCommad);
                        }
                    }
                    else
                    {
                        internalCT.ThrowIfCancellationRequested();
                        await Task.Delay(1000, internalCT).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("EXCEPTION: Messenger - StartDecoding: Operation was canceled.");
                _decoderRunning = false;
                return;
            }
        }

        public void StopReceiving()
        {
            _receiveCTS.Cancel();
            _device.DiscardInBuffer();
            _receiveCTS.Dispose();
            _receiveCTS = new CancellationTokenSource();
        }

        public void StopDecoding()
        {
            _decodingCTS.Cancel();
            _decodingCTS.Dispose();
            _decodingCTS = new CancellationTokenSource();
        }

        public async Task<bool> SendAsync(ICommandModel command)
        {
            _isSending = true;
            bool status = await _device.SendAsync(_commandTranslator.FromCommand(command)).ConfigureAwait(false);
            _isSending = false;
            return status;
        }

        public async Task<bool> SendAsync(IMessageModel message)
        {
            if (message is null)
                return false;

            List<bool> results = new List<bool>();

            foreach (ICommandModel c in message)
            {
                results.Add(await _device.SendAsync(_commandTranslator.FromCommand(c)).ConfigureAwait(false));
            }

            if (results.Contains(false))
                return false;
            return true;
        }

        public bool Send(IMessageModel message)
        {
            if (message is null)
                return false;

            bool wasSentWithoutError = true;

            foreach (ICommandModel c in message)
            {
                if (_device.Send(_commandTranslator.FromCommand(c)))
                    continue;
                else
                    wasSentWithoutError = false;
            }
            return wasSentWithoutError;
        }

        protected virtual void OnCommandReceived(ICommandModel command)
        {
            CommandReceived?.Invoke(this, command);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // free managed resources
                if (_receiveCTS != null)
                {
                    _receiveCTS?.Cancel();
                    _receiveCTS?.Dispose();
                    _receiveCTS = null;
                }
                if (_decodingCTS != null)
                {
                    _receiveCTS?.Cancel();
                    _receiveCTS?.Dispose();
                    _receiveCTS = null;
                }
                if (_rawDataBuffer != null)
                {
                    _rawDataBuffer?.Dispose();
                    _rawDataBuffer = null;
                }
                if (_device != null)
                {
                    _device?.Dispose();
                    _device = null;
                }
            }

            _disposed = true;
            // free native resources if there are any.
            //if (nativeResource != IntPtr.Zero)
            //{
            //}
        }
    }
}