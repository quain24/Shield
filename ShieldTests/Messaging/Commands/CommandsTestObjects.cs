﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shield.Messaging.Commands;
using Shield.Messaging.RawData;
using Shield.Timestamps;
using ShieldTests.Messaging.Commands.Parts;

namespace ShieldTests.Messaging.Commands
{
    public static class CommandsTestObjects
    {
        public static CommandFactory GetProperAlwaysValidCommandFactory()
        {
            var partFactory = PartFactoryTestObjects.GetAlwaysValidPartFactory();
            var comfacadap = new CommandFactoryAutoFacAdapter((id, hostid, target, order, data, Timestamp) => new Command(id, hostid, target, order, data, TimestampFactory.Timestamp));

            return new CommandFactory(DefaultProtocolValues.Separator, partFactory, comfacadap, new IdGenerator(DefaultProtocolValues.IDLength));
        }

        public static ICommand GetProperTestCommand_order()
        {
            var commandFac = GetProperAlwaysValidCommandFactory();

            return commandFac.TranslateFrom(new RawCommand("00ID*HOSTID*TARGET*ORDER*DATA"));
        }

        public static ICommand GetProperTestCommand_confirmation()
        {
            var commandFac = GetProperAlwaysValidCommandFactory();

            return commandFac.TranslateFrom(new RawCommand($"01ID*HOSTID*{Shield.GlobalConfig.DefaultTargets.ConfirmationTarget}*00ID*Valid"));
        }

        public static ICommand GetProperTestCommand_reply()
        {
            var commandFac = GetProperAlwaysValidCommandFactory();

            return commandFac.TranslateFrom(new RawCommand($"01ID*HOSTID*{Shield.GlobalConfig.DefaultTargets.ReplyTarget}*00ID*ReplyData"));
        }
    }
}
