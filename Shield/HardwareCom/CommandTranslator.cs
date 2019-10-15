﻿using Shield.Data;
using Shield.Data.Models;
using Shield.Enums;
using Shield.HardwareCom.Models;
using System;
using System.Text;

namespace Shield.HardwareCom
{
    /// <summary>
    /// Translates given CommandModel into 'string' of raw data that could be sent and vice versa.
    /// </summary>
    public class CommandTranslator : ICommandTranslator
    {
        private readonly char _separator;
        private readonly char _filler;

        private Func<ICommandModel> _commandModelFac;
        private IApplicationSettingsModel _appSettingsModel;

        public CommandTranslator(IAppSettings appSettings, Func<ICommandModel> commandModelFac)
        {
            _commandModelFac = commandModelFac; // Autofac autofactory
            _appSettingsModel = appSettings.GetSettingsFor<IApplicationSettingsModel>();
            _separator = _appSettingsModel.Separator;
            _filler = _appSettingsModel.Filler;
        }

        public ICommandModel FromString(string rawData)
        {
            ICommandModel command = _commandModelFac();
            string rawCommandTypeString = string.Empty;
            string rawDataString = string.Empty;
            string rawIdString = string.Empty;

            int CommandLengthWithData = _appSettingsModel.CommandTypeSize + _appSettingsModel.IdSize + _appSettingsModel.DataSize + 3;
            int CommandLength = _appSettingsModel.CommandTypeSize + _appSettingsModel.IdSize + 3;

            if (rawData.Length == CommandLengthWithData || rawData.Length == CommandLength)
            {
                rawCommandTypeString = rawData.Substring(1, _appSettingsModel.CommandTypeSize);
                rawIdString = rawData.Substring(2 + _appSettingsModel.CommandTypeSize, _appSettingsModel.IdSize);

                if(rawData.Length == CommandLengthWithData)
                    rawDataString = rawData.Substring(3 + _appSettingsModel.CommandTypeSize + _appSettingsModel.IdSize);                

                int rawComInt;
                if (Int32.TryParse(rawCommandTypeString, out rawComInt))
                {
                    if (Enum.IsDefined(typeof(CommandType), rawComInt))
                        command.CommandType = (CommandType)rawComInt;
                    else
                        command.CommandType = CommandType.Unknown;

                    command.Id = rawIdString;
                    command.Data = rawData.Length == CommandLengthWithData ? rawDataString : string.Empty;
                }
                else
                {
                    command.CommandType = CommandType.Error;
                    command.Id = string.Empty.PadLeft(_appSettingsModel.IdSize, _filler);
                    command.Data = rawData.Length > _appSettingsModel.DataSize ? rawData.Substring(0, _appSettingsModel.DataSize) : rawData;
                }
            }
            else
            {
                command.CommandType = CommandType.Error;
                command.Id = string.Empty.PadLeft(_appSettingsModel.IdSize, _filler);
                command.Data = string.Empty.PadLeft(_appSettingsModel.DataSize, _filler);
            }

            return command;
        }

        /// <summary>
        /// Translates a CommandModel into a raw formatted string if given a correct command or returns empty string for error
        /// </summary>
        /// <param name="givenCommand">Command to be trasformed into raw string</param>
        /// <returns>Raw formatted string that can be understood by connected machine</returns>
        public string FromCommand(ICommandModel givenCommand)
        {
            int completeCommandSizeWithSep = _appSettingsModel.CommandTypeSize + 2 + _appSettingsModel.IdSize + 1 + _appSettingsModel.DataSize;

            if (givenCommand is null || !Enum.IsDefined(typeof(CommandType), givenCommand.CommandType))
                return null;

            StringBuilder command = new StringBuilder(_separator.ToString());

            command.Append(((int)givenCommand.CommandType).ToString().PadLeft(_appSettingsModel.CommandTypeSize, '0')).Append(_separator);
            command.Append(givenCommand.Id).Append(_separator);

            if (givenCommand.CommandType == CommandType.Data)
                command.Append(givenCommand.Data);

            if (command.Length < completeCommandSizeWithSep)
                command.Append(_filler, completeCommandSizeWithSep - command.Length);

            return command.ToString();
        }
    }
}