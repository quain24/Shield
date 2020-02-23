﻿using Shield.HardwareCom.Factories;
using Shield.HardwareCom.MessageProcessing;
using Shield.HardwareCom.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Shield.HardwareCom
{
    internal class CommandIngester : ICommandIngester
    {
        private readonly IMessageFactory _msgFactory;
        private ICompleteness _completness;
        private ITimeoutCheck _completitionTimeout;

        private readonly BlockingCollection<ICommandModel> _awaitingQueue = new BlockingCollection<ICommandModel>();

        private readonly Dictionary<string, IMessageModel> _incompleteMessages =
            new Dictionary<string, IMessageModel>(StringComparer.InvariantCultureIgnoreCase);

        private readonly BlockingCollection<IMessageModel> _processedMessages = new BlockingCollection<IMessageModel>();
        private readonly BlockingCollection<ICommandModel> _errCommands = new BlockingCollection<ICommandModel>();

        private readonly object _lock = new object();
        private readonly object _timeoutCheckLock = new object();
        private bool _isProcessing = false;
        private bool _isTimeoutChecking = false;
        private CancellationTokenSource _cancelProcessingCTS = new CancellationTokenSource();
        private CancellationTokenSource _cancelTimeoutCheckCTS = new CancellationTokenSource();

        /// <summary>
        /// Returns true if commands are being processed or object waits for new commands to be processed
        /// </summary>
        public bool IsProcessingCommands => _isProcessing;

        /// <summary>
        /// Will ingest given commands into corresponding messages or will create new messages for those commands.
        /// If commands is to be ingested into already completed message - it will be put in separate collection.
        /// </summary>
        /// <param name="messageFactory">Message factory delegate</param>
        /// <param name="completeness">State check - checks if message is completed</param>
        /// <param name="completitionTimeout">State check - optional - checks if completition time is exceeded</param>
        public CommandIngester(IMessageFactory messageFactory, ICompleteness completeness, ITimeoutCheck completitionTimeout = null)
        {
            _msgFactory = messageFactory;
            _completness = completeness;
            _completitionTimeout = completitionTimeout;
        }

        /// <summary>
        /// Add single command to be processed / injected into corresponding / new message in internal collection.
        /// Thread safe
        /// </summary>
        /// <param name="command">command object to be ingested</param>
        /// <returns>true if success</returns>
        public bool AddCommandToProcess(ICommandModel command)
        {
            if (command is null)
                return false;
            _awaitingQueue.Add(command);
            Console.WriteLine($@"CommandIngester - Command {command.Id} added to be processed.");
            return true;
        }

        /// <summary>
        /// Start processing of commands that were added to internal collection by <code>AddCommandToProcess</code>
        /// Thread safe
        /// </summary>
        public void StartProcessingCommands()
        {
            if (!CanStartProcessing())
                return;

            Debug.WriteLine("CommandIngester - Command processing started");

            while (true)
            {
                try
                {
                    bool a = _awaitingQueue.TryTake(out ICommandModel command, -1, _cancelProcessingCTS.Token);

                    if (a) Console.WriteLine($"CommandIngester - Took command {command.Id} for processing");
                    TryIngest(command, out IMessageModel message);
                }
                catch
                {
                    break;
                }
            }
            Debug.WriteLine("CommandIngester - StartProcessingCommands ENDED");
            _isProcessing = false;
        }

        private bool CanStartProcessing()
        {
            lock (_lock)
                return _isProcessing
                    ? false
                    : _isProcessing = true;
        }

        /// <summary>
        /// Tries to ingest command into a message in internal collection if this message is not completed
        /// or there is no such message - in which case a new message is created and added to collection with this given command.
        /// </summary>
        /// <param name="incomingCommand">Command to be ingested</param>
        /// <param name="message">Message that the command was ingested into</param>
        /// <returns>True if command was ingested, false if a corresponding message was already completed</returns>
        private bool TryIngest(ICommandModel incomingCommand, out IMessageModel message)
        {
            _ = _completness ?? throw new NullReferenceException($@"{nameof(_completness)} is NULL");
            _ = incomingCommand ?? throw new ArgumentNullException(nameof(incomingCommand));

            Debug.WriteLine($@"CommandIngester TryIngest command {incomingCommand.Id}");

            SetIdAsUsedUp(incomingCommand.Id);

            if (!TryGetExistingMessage(incomingCommand.Id, out message))
            {
                message = CreateNewIncomingMessage(incomingCommand.Id);
                _incompleteMessages.Add(message.Id, message);
            }

            if (IsMessageNullOrAlreadyComplete(message))
            {
                _errCommands.Add(incomingCommand);
                Debug.WriteLine("CommandIngester - ERROR - tried to add new command to completed / null message");
                return false;
            }

            // check for completition timeout if checker exists
            if (_completitionTimeout?.IsExceeded(message) ?? false)
            {
                HandleMessageTimeout(message, incomingCommand);
                return false;
            }

            message.Add(incomingCommand);

            // if completed after last command, then move to processed
            if (IsMessageNullOrAlreadyComplete(message))
                PushFromIncompleteToProcessed(message);

            return true;
        }

        private void SetIdAsUsedUp(string id)
        {
            if (!string.IsNullOrWhiteSpace(id))
                Helpers.IdGenerator.UsedThisID(id);
        }

        private bool TryGetExistingMessage(string id, out IMessageModel message)
        {
            if (_incompleteMessages.ContainsKey(id))
            {
                Debug.WriteLine("CommandIngester - Incomplete message with command id found!");
                message = _incompleteMessages[id];
                return true;
            }
            message = null;
            return false;
        }

        private IMessageModel CreateNewIncomingMessage(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Id cannot be empty", nameof(id));

            IMessageModel message = _msgFactory.CreateNew(direction: Enums.Direction.Incoming, id: id);
            Debug.WriteLine($@"CommandIngester - new {id} message created");
            return message;
        }

        private bool IsMessageNullOrAlreadyComplete(IMessageModel message) =>
            message is null || _completness.IsComplete(message);

        private void HandleMessageTimeout(IMessageModel message, ICommandModel lastCommand = null)
        {
            message.Errors |= Enums.Errors.CompletitionTimeout;
            PushFromIncompleteToProcessed(message);
            if (lastCommand != null)
                _errCommands.Add(lastCommand);
            Debug.WriteLine($@"ContinousTimeoutChecker - Error - message {message.Id} completition timeout");
        }

        private void PushFromIncompleteToProcessed(IMessageModel message)
        {
            _ = message ?? throw new ArgumentNullException(nameof(message));

            _processedMessages.Add(message);
            _incompleteMessages[message.Id] = null;
            Debug.WriteLine($@"CommandIngester - Message {message.Id} was processed, adding to processed messages collection");
        }

        /// <summary>
        /// Stops processing commands
        /// </summary>
        public void StopProcessingCommands()
        {
            _cancelProcessingCTS.Cancel();
            _cancelProcessingCTS = new CancellationTokenSource();
        }

        public async Task StartTimeoutCheckAsync(int interval = 0)
        {
            if (!CanStartTiemoutCheck())
                return;

            int checkInterval = CalcCheckInterval(interval);

            try
            {
                while (true)
                {
                    _cancelTimeoutCheckCTS.Token.ThrowIfCancellationRequested();

                    foreach (var message in TimeouttedMessagesList())
                    {
                        HandleMessageTimeout(message);
                        _cancelTimeoutCheckCTS.Token.ThrowIfCancellationRequested();
                    }

                    await Task.Delay(checkInterval, _cancelTimeoutCheckCTS.Token).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                if (!IsTimeoutCheckCorrectlyCancelled(e))
                    throw;
            }
        }

        private bool CanStartTiemoutCheck()
        {
            if (_completitionTimeout is null)
                return false;

            lock (_timeoutCheckLock)
            {
                if (!_isTimeoutChecking)
                    return _isTimeoutChecking = true;
                else
                    return false;
            }
        }

        private int CalcCheckInterval(long timeout)
        {
            switch (timeout)
            {
                case var _ when timeout <= _completitionTimeout.NoTimeoutValue:
                return 250;

                case var _ when timeout <= 100:
                return 10;

                case var _ when timeout <= 3000:
                return 100;

                case var _ when timeout > 3000:
                return 250;

                default:
                return _completitionTimeout.NoTimeoutValue;
            }
        }

        private List<IMessageModel> TimeouttedMessagesList() =>
            _completitionTimeout?.GetTimeoutsFromCollection(GetIncompletedMessages());

        private bool IsTimeoutCheckCorrectlyCancelled(Exception e)
        {
            lock (_timeoutCheckLock)
                _isTimeoutChecking = false;

            return (e is TaskCanceledException || e is OperationCanceledException) ? true : false;
        }

        public void StopTimeoutCheck()
        {
            _cancelTimeoutCheckCTS.Cancel();
            _cancelTimeoutCheckCTS = new CancellationTokenSource();
        }

        /// <summary>
        /// Gives a thread safe collection of completed and timeout messages for further processing
        /// </summary>
        /// <returns></returns>
        public BlockingCollection<IMessageModel> GetProcessedMessages()
        {
            Console.WriteLine($@"CommandIngester - Requested Processed Messages ({_processedMessages.Count} available)");
            return _processedMessages;
        }

        public Dictionary<string, IMessageModel> GetIncompletedMessages() => _incompleteMessages;

        /// <summary>
        /// Gives a thread safe collection of commands that were received, but corresponding messages were already completed or completition timeout
        /// was exceeded.
        /// </summary>
        /// <returns></returns>
        public BlockingCollection<ICommandModel> GetErrAlreadyCompleteOrTimeout() => _errCommands;
    }
}