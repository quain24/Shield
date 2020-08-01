﻿using Xunit;
using Shield.Messaging.Protocol;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Shield.Messaging.Commands;
using ShieldTests.Extensions;
using ShieldTests.Protocol;
using Xunit.Abstractions;

namespace Shield.Messaging.Protocol.Tests
{
    public class ResponseAwaiterDispatchTests
    {
        private readonly ITestOutputHelper _output;

        public ResponseAwaiterDispatchTests(ITestOutputHelper output)
        {
            _output = output;
            Setup();
        }

        public ResponseAwaiterDispatch Dispatch;
        public Order NormalOrder;
        public Confirmation NormalConfirmation;
        public Reply NormalReply;


        public void Setup()
        {
            
            Dispatch = new ResponseAwaiterDispatch(ResponseAwaiterDispatchTestObjects.GetProperAwaitersDictionary());
            NormalOrder = ProtocolTestObjects.GetNormalOrder();
            NormalConfirmation = ProtocolTestObjects.GetNormalConfirmation();
            NormalReply = ProtocolTestObjects.GetNormalReply();
        }

        [Fact()]
        public void Will_throw_Exception_if_is_given_null_instead_of_proper_awaiter_collection()
        {
            var exception = Record.Exception(() => new ResponseAwaiterDispatch(null));
            
            _output.AddMessageFrom(exception);
            
            Assert.IsType<ArgumentOutOfRangeException>(exception);

        }

        [Fact()]
        public void Will_throw_if_is_given_not_complete_list_of_needed_awaiters()
        {
            var exception = Record.Exception(() =>
                new ResponseAwaiterDispatch(ResponseAwaiterDispatchTestObjects.GetIAwatersDictionaryWithoutReply()));

            _output.AddMessageFrom(exception);

            Assert.IsType<ArgumentOutOfRangeException>(exception);

        }

        [Fact()]
        public async Task Returns_true_when_order_was_replied_to_in_time()
        {
            var result = Dispatch.RepliedToInTimeAsync(NormalOrder).ConfigureAwait(false);
            Dispatch.AddResponse(NormalReply);

            Assert.True(await result);
        }

        [Fact()]
        public async Task Returns_true_when_order_was_confirmed_in_time()
        {
            var result = Dispatch.ConfirmedInTimeAsync(NormalOrder).ConfigureAwait(false);
            Dispatch.AddResponse(NormalConfirmation);

            Assert.True(await result);
        }

        [Fact()]
        public async Task Should_return_false_if_not_given_reply_to_order_in_time()
        {
            var result = await Dispatch.RepliedToInTimeAsync(NormalOrder).ConfigureAwait(false);

            Assert.False(result);
        }

        [Fact()]
        public async Task Should_return_false_if_not_given_confirmation_to_order_in_time()
        {
            var result = await Dispatch.ConfirmedInTimeAsync(NormalOrder).ConfigureAwait(false);

            Assert.False(result);
        }

        [Fact()]
        public void Should_return_proper_reply_when_asked()
        {
            Dispatch.RepliedToInTimeAsync(NormalOrder);
            Dispatch.AddResponse(NormalReply);

            var result = Dispatch.ReplyTo(NormalOrder);

            Assert.IsType<Reply>(result);
            Assert.Equal(NormalOrder.ID, result.ReplysTo);
        }

        [Fact()]
        public void Should_return_proper_confirmation_when_asked()
        {
            Dispatch.ConfirmedInTimeAsync(NormalOrder);
            Dispatch.AddResponse(NormalConfirmation);

            var result = Dispatch.ConfirmationOf(NormalOrder);

            Assert.IsType<Confirmation>(result);
            Assert.Equal(NormalOrder.ID, result.Confirms);
        }

        [Fact()]
        public void Should_throw_when_given_null_as_response()
        {
            var exception = Record.Exception(() =>
                Dispatch.AddResponse(null));
            _output.AddMessageFrom(exception);

            Assert.IsType<ArgumentNullException>(exception);
        }

        [Fact()]
        public void Should_throw_when_given_unknown_type_of_response()
        {
            var exception = Record.Exception(() =>
                Dispatch.AddResponse(ResponseAwaiterDispatchTestObjects.GetResponseMessageOfUnknownType()));

            _output.AddMessageFrom(exception);

            Assert.IsType<ArgumentOutOfRangeException>(exception);
        }
    }
}