﻿using Caliburn.Micro;
using Shield.Data;
using Shield.Data.Models;
using Shield.Enums;
using Shield.HardwareCom;
using Shield.HardwareCom.Factories;
using Shield.HardwareCom.Models;
using Shield.Helpers;
using Shield.WpfGui.Validators;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Shield.WpfGui.ViewModels
{
    public class ShellViewModel : Conductor<object>, INotifyDataErrorInfo
    {
        private ISettings _settings;
        private readonly ICommunicationDeviceFactory _communicationDeviceFactory;
        private readonly ICommandModelFactory _commandFactory;
        private readonly IMessageFactory _messageFactory1;
        private readonly IIdGenerator _idGenerator;
        private readonly IMessengingPipelineFactory _incomingMessagePipelineFactory;
        private readonly MessengingPipeline _pipeline;
        private string _selectedCommand;
        private string _dataInput;

        private BindableCollection<string> _possibleCommands = new BindableCollection<string>(Enum.GetNames(typeof(CommandType)));
        private BindableCollection<IMessageModel> _receivedMessages = new BindableCollection<IMessageModel>();
        private BindableCollection<ICommandModel> _newMessageCommands = new BindableCollection<ICommandModel>();
        private BindableCollection<IMessageModel> _sentMessages = new BindableCollection<IMessageModel>();
        private ICommandModel _selectedNewMessageCommand;
        private IMessageModel _selectedSentMessage;

        private Func<IMessageModel> _messageFactory;

        private IMessageModel _selectedReceivedMessage;

        private bool _receivingButtonActivated = false;
        private bool _sending = false;
        private bool _openingError = false;

        private CommandDataPackValidation _dataPackValidation;

        private readonly Dictionary<string, ICollection<string>>
        _validationErrors = new Dictionary<string, ICollection<string>>();

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public ShellViewModel(IMessengingPipelineFactory incomingMessagePipelineFactory, ISettings settings,
                              ICommunicationDeviceFactory deviceFactory, ICommandModelFactory commandFactory,
                              IMessageFactory messageFactory,
                              IIdGenerator idGenerator)
        {
            _incomingMessagePipelineFactory = incomingMessagePipelineFactory ?? throw new ArgumentNullException(nameof(incomingMessagePipelineFactory));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _communicationDeviceFactory = deviceFactory ?? throw new ArgumentNullException(nameof(deviceFactory));
            _commandFactory = commandFactory ?? throw new ArgumentNullException(nameof(commandFactory));
            _messageFactory1 = messageFactory ?? throw new ArgumentNullException(nameof(messageFactory));
            _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
            _settings.LoadFromFile();
            _settings.SaveToFile();

            _pipeline = _incomingMessagePipelineFactory.GetPipelineFor(_communicationDeviceFactory.CreateDevice("COM4"));

            _dataPackValidation = new CommandDataPackValidation(_settings.ForTypeOf<IApplicationSettingsModel>().Separator, DataPackFiller());

            // Updating table in gui
            Task.Run(async () =>
            {
                while (true)
                {
                    IMessageModel message = _pipeline.GetReceivedMessages().Take();
                    AddIncomingMessageToDisplay(this, message);
                }
            });
        }

        public int DataPackLength()
        {
            return _settings.ForTypeOf<IApplicationSettingsModel>().DataSize;
        }

        public char DataPackFiller()
        {
            return _settings.ForTypeOf<IApplicationSettingsModel>().Filler;
        }

        public List<string> DataPackGenerator(string data)
        {
            int dataPackLength = DataPackLength();
            char filler = DataPackFiller();

            List<string> packs = Enumerable.Range(0, data.Length / DataPackLength())
                .Select(i => data.Substring(i * DataPackLength(), DataPackLength()))
                .ToList();

            int packsCumulativeLength = packs.Aggregate(0, (count, val) => count + val.Length);
            if (packsCumulativeLength < data.Length)
            {
                string lastOne = data.Substring(packsCumulativeLength);
                packs.Add(lastOne);
            }

            if (packs.Last().Length < dataPackLength)
                packs[packs.Count - 1] = packs.Last().PadRight(dataPackLength, filler);
            return packs;
        }

        public void AddIncomingMessageToDisplay(object sender, IMessageModel e)
        {
            ReceivedMessages.Add(e);
        }

        public void AddIncomingMessageErrorToDisplay(object sender, IMessageModel e)
        {
            ReceivedMessages.Add(e);
        }

        public BindableCollection<IMessageModel> ReceivedMessages
        {
            get => _receivedMessages;
            set => _receivedMessages = value;
        }

        public IMessageModel SelectedReceivedMessage
        {
            get => _selectedReceivedMessage;
            set
            {
                _selectedReceivedMessage = value;
                NotifyOfPropertyChange(() => SelectedReceivedMessage);
                NotifyOfPropertyChange(() => SingleMessageCommands);
            }
        }

        public BindableCollection<ICommandModel> SingleMessageCommands
        {
            get
            {
                return GetSingleMessageCommands();
            }
        }

        public BindableCollection<ICommandModel> GetSingleMessageCommands()
        {
            var output = new BindableCollection<ICommandModel>();

            if (_selectedReceivedMessage is null)
                return output;

            foreach (var c in SelectedReceivedMessage)
            {
                output.Add(c);
            }

            return output;
        }

        public bool CanOpenDevice
        {
            get
            {
                if (_pipeline.IsOpen) return false;
                return true;
            }
        }

        public bool CanCloseDevice
        {
            get
            {
                if (_pipeline is null) return false;
                if (_pipeline.IsOpen) return true;
                if (_openingError)
                {
                    _openingError = false;
                    return true;
                }
                return false;
            }
        }

        public void OpenDevice()
        {
            try
            {
                //_messanger.Open();
                _pipeline.Open();
                NotifyOfPropertyChange(() => CanOpenDevice);
                NotifyOfPropertyChange(() => CanCloseDevice);
                NotifyOfPropertyChange(() => CanStartReceiving);
                NotifyOfPropertyChange(() => CanStopReceiving);
                NotifyOfPropertyChange(() => ButtonAIsChecked);
                NotifyOfPropertyChange(() => CanStartReceiving);
                NotifyOfPropertyChange(() => CanSendMessage);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message);

                // Enables Close port button in case of exception, enables CanOpenDevice to return TRUE
                _openingError = true;
                NotifyOfPropertyChange(() => CanOpenDevice);
                NotifyOfPropertyChange(() => CanCloseDevice);
            }
        }

        public void CloseDevice()
        {
            //_messanger.Close();
            _pipeline.Close();
            NotifyOfPropertyChange(() => CanCloseDevice);
            NotifyOfPropertyChange(() => CanOpenDevice);
            NotifyOfPropertyChange(() => CanStartReceiving);
            NotifyOfPropertyChange(() => CanStopReceiving);
            NotifyOfPropertyChange(() => ButtonAIsChecked);
            NotifyOfPropertyChange(() => CanSendMessage);
        }

        public bool CanStartReceiving
        {
            get
            {
                if (_receivingButtonActivated == false && _pipeline.IsOpen) return true;
                return false;
            }
        }

        public bool ButtonAIsChecked => _pipeline.IsOpen ? true : _receivingButtonActivated = false;

        public void StartReceiving()
        {
            //Task.Run(() => _messanger.StartReceiveingAsync());
            ////Task.Run(() => _messanger.StartDecoding());

            //Task.Run(() => _commandIngester.StartProcessingCommands()).ConfigureAwait(false);
            //Task.Run(() => _commandIngester.StartProcessingCommands()).ConfigureAwait(false);
            //Task.Run(() => _commandIngester.StartProcessingCommands()).ConfigureAwait(false);
            //Task.Run(() => _commandIngester.StartTimeoutCheckAsync().ConfigureAwait(false)).ConfigureAwait(false);
            //Task.Run(() => _incomingMessageProcessor.StartProcessingMessagesContinous()).ConfigureAwait(false);

            _receivingButtonActivated = true;
            NotifyOfPropertyChange(() => CanStartReceiving);
            NotifyOfPropertyChange(() => CanStopReceiving);
        }

        public bool CanStopReceiving
        {
            get
            {
                if (_receivingButtonActivated == true && _pipeline.IsOpen) return true;
                return false;
            }
        }

        public void StopReceiving()
        {
            _pipeline.Close();
            _receivingButtonActivated = false;
            NotifyOfPropertyChange(() => CanStartReceiving);
            NotifyOfPropertyChange(() => CanStopReceiving);
        }

        public BindableCollection<string> CommandTypes
        {
            set => _possibleCommands = value;
            get => _possibleCommands;
        }

        public string SelectedCommand
        {
            get => _selectedCommand;
            set
            {
                _selectedCommand = value;
                NotifyOfPropertyChange(() => SelectedCommand);
                NotifyOfPropertyChange(() => DataInputState);
                NotifyOfPropertyChange(() => CanAddCommand);
            }
        }

        public bool DataInputState
        {
            get
            {
                if (_selectedCommand == GetNameFromEnumValue(CommandType.Data))
                    return true;
                return false;
            }
        }

        private string GetNameFromEnumValue(CommandType value)
        {
            return Enum.GetName(value.GetType(), value);
        }

        public string DataInput
        {
            get => _dataInput;
            set
            {
                _dataInput = value;
                ValidateCommandDataPack(_dataInput);
                NotifyOfPropertyChange(() => DataInput);
                NotifyOfPropertyChange(() => CanAddCommand);
            }
        }

        public void AddCommand()
        {
            if (SelectedCommand is null)
                return;

            var commands = new List<ICommandModel>();

            if (SelectedCommand == GetNameFromEnumValue(CommandType.Data))
            {
                List<string> packs = DataPackGenerator(DataInput);

                packs.ForEach(pack =>
                {
                    ICommandModel command = _commandFactory.Create(CommandType.Data);
                    command.Data = pack;
                    commands.Add(command);
                });

                NewMessageCommands.AddRange(commands);
            }
            else
                NewMessageCommands.Add(_commandFactory.Create((CommandType)Enum.Parse(typeof(CommandType), SelectedCommand)));
            NotifyOfPropertyChange(() => CanSendMessage);
            NotifyOfPropertyChange(() => NewMessageCommands);
        }

        public bool CanAddCommand
        {
            get
            {
                if (SelectedCommand == GetNameFromEnumValue(CommandType.Data))
                {
                    if (_validationErrors.ContainsKey("DataInput") && _validationErrors["DataInput"].Count > 0)
                        return false;

                    if (DataInput != null && DataInput.Length > 0 && !DataInput.Contains(DataPackFiller()) &&
                        !DataInput.Contains(_settings.ForTypeOf<IApplicationSettingsModel>().Separator) &&
                        !DataInput.Contains(" "))
                    {
                        return true;
                    }
                    return false;
                }
                return true;
            }
        }

        public bool CanAddCommandEventHandler()
        {
            if (CanAddCommand)
            {
                AddCommand();
                return true;
            }
            return false;
        }

        public BindableCollection<ICommandModel> NewMessageCommands
        {
            get => _newMessageCommands;
            set
            {
                _newMessageCommands = value;
                NotifyOfPropertyChange(() => CanRemoveCommand);
                NotifyOfPropertyChange(() => NewMessageCommands);
            }
        }

        public BindableCollection<IMessageModel> SentMessages
        {
            get => _sentMessages;
            set
            {
                _sentMessages = value;
                // hack!
                _sending = false;
            }
        }

        public ICommandModel SelectedNewMessageCommand
        {
            get => _selectedNewMessageCommand;

            set
            {
                _selectedNewMessageCommand = value;
                NotifyOfPropertyChange(() => SelectedNewMessageCommand);
                NotifyOfPropertyChange(() => CanRemoveCommand);
            }
        }

        public IMessageModel SelectedSentMessage
        {
            get => _selectedSentMessage;

            set
            {
                _selectedSentMessage = value;
                NotifyOfPropertyChange(() => SelectedSentMessage);
                NotifyOfPropertyChange(() => SingleSelectedSentMessage);
            }
        }

        public BindableCollection<ICommandModel> SingleSelectedSentMessage
        {
            get
            {
                var output = new BindableCollection<ICommandModel>();

                if (SelectedSentMessage is null)
                    return output;

                foreach (var c in SelectedSentMessage)
                    output.Add(c);

                return output;
            }
        }

        public void RemoveCommand()
        {
            NewMessageCommands.Remove(SelectedNewMessageCommand);
            NotifyOfPropertyChange(() => CanSendMessage);
        }

        public bool CanRemoveCommand
        {
            get => SelectedNewMessageCommand is null ? false : true;
        }

        public async Task SendMessage()
        {
            var message = GenerateMessage(NewMessageCommands);
            if (message is null)
                return;

            // hack!
            _sending = true;
            NotifyOfPropertyChange(() => CanSendMessage);

            bool sent = await _pipeline.SendAsync(message).ConfigureAwait(false);

            // hack
            _sending = false;
            if (sent)
            {
                SentMessages.Add(message);

                NewMessageCommands.Clear();
                NotifyOfPropertyChange(() => NewMessageCommands);
                NotifyOfPropertyChange(() => SentMessages);
                NotifyOfPropertyChange(() => CanSendMessage);
            }
        }

        public bool CanSendMessage
        {
            get
            {
                if (NewMessageCommands.Count < 1 || _pipeline.IsOpen == false || _sending == true)
                    return false;

                return true;
            }
        }

        private IMessageModel GenerateMessage(IEnumerable<ICommandModel> commands)
        {
            if (commands.Count() == 0 || commands is null)
                return null;

            IMessageModel message = _messageFactory();

            foreach (var c in commands)
            {
                message.Add(c);
            }

            message.Timestamp = Timestamp.TimestampNow;
            message.AssaignID(_idGenerator.GetNewID());

            return message;
        }

        public bool HasErrors
        {
            get => _validationErrors.Count > 0;
        }

        private async void ValidateCommandDataPack(string data)
        {
            const string propertyKey = "DataInput";
            ICollection<string> validationErrors = null;
            /* Call service asynchronously */
            bool isValid = await Task<bool>.Run(() =>
            {
                return _dataPackValidation.ValidateDataPack(data, out validationErrors);
            })
            .ConfigureAwait(false);

            if (!isValid)
            {
                /* Update the collection in the dictionary returned by the GetErrors method */
                _validationErrors[propertyKey] = validationErrors;
                /* Raise event to tell WPF to execute the GetErrors method */
                RaiseErrorsChanged(propertyKey);
            }
            else if (_validationErrors.ContainsKey(propertyKey))
            {
                /* Remove all errors for this property */
                _validationErrors.Remove(propertyKey);
                /* Raise event to tell WPF to execute the GetErrors method */
                RaiseErrorsChanged(propertyKey);
            }
        }

        public IEnumerable GetErrors(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)
                || !_validationErrors.ContainsKey(propertyName))
                return null;

            return _validationErrors[propertyName];
        }

        private void RaiseErrorsChanged(string propertyName)
        {
            if (ErrorsChanged != null)
                ErrorsChanged(this, new DataErrorsChangedEventArgs(propertyName));
        }
    }
}