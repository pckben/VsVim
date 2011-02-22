﻿using System;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Moq;
using NUnit.Framework;
using Vim;
using Vim.Extensions;
using Vim.Modes;
using Vim.UnitTest;
using Vim.UnitTest.Mock;
using GlobalSettings = Vim.GlobalSettings;

namespace VimCore.UnitTest
{
    [TestFixture]
    public sealed class CommandUtilTest
    {
        private MockRepository _factory;
        private Mock<IVimHost> _vimHost;
        private Mock<IStatusUtil> _statusUtil;
        private Mock<ICommonOperations> _operations;
        private IVimGlobalSettings _globalSettings;
        private IVimLocalSettings _localSettings;
        private ITextViewMotionUtil _motionUtil;
        private IRegisterMap _registerMap;
        private IVimData _vimData;
        private IMarkMap _markMap;
        private ITextView _textView;
        private ITextBuffer _textBuffer;
        private CommandUtil _commandUtil;

        private void Create(params string[] lines)
        {
            _factory = new MockRepository(MockBehavior.Loose);
            _vimHost = _factory.Create<IVimHost>();
            _statusUtil = _factory.Create<IStatusUtil>();
            _operations = _factory.Create<ICommonOperations>();
            _operations
                .Setup(x => x.WrapEditInUndoTransaction(It.IsAny<string>(), It.IsAny<FSharpFunc<Unit, Unit>>()))
                .Callback<string, FSharpFunc<Unit, Unit>>((x, y) => y.Invoke(null));

            _textView = EditorUtil.CreateView(lines);
            _textBuffer = _textView.TextBuffer;
            _vimData = new VimData();
            _registerMap = VimUtil.CreateRegisterMap(MockObjectFactory.CreateClipboardDevice().Object);
            _markMap = new MarkMap(new TrackingLineColumnService());
            _globalSettings = new GlobalSettings();
            _localSettings = new LocalSettings(_globalSettings, _textView);

            var localSettings = new LocalSettings(new Vim.GlobalSettings());
            _motionUtil = VimUtil.CreateTextViewMotionUtil(
                _textView,
                settings: localSettings,
                vimData: _vimData);
            _commandUtil = VimUtil.CreateCommandUtil(
                _textView,
                _operations.Object,
                _motionUtil,
                statusUtil: _statusUtil.Object,
                registerMap: _registerMap,
                markMap: _markMap,
                vimData: _vimData);
        }

        private Register UnnamedRegister
        {
            get { return _registerMap.GetRegister(RegisterName.Unnamed); }
        }

        private void SetLastCommand(NormalCommand command, int? count = null, RegisterName name = null)
        {
            var data = VimUtil.CreateCommandData(count, name);
            var storedCommand = StoredCommand.NewNormalCommand(command, data, CommandFlags.None);
            _vimData.LastCommand = FSharpOption.Create(storedCommand);
        }

        [Test]
        public void ReplaceChar1()
        {
            Create("foo");
            _commandUtil.ReplaceChar(KeyInputUtil.CharToKeyInput('b'), 1);
            Assert.AreEqual("boo", _textView.TextSnapshot.GetLineFromLineNumber(0).GetText());
        }

        [Test]
        public void ReplaceChar2()
        {
            Create("foo");
            _commandUtil.ReplaceChar(KeyInputUtil.CharToKeyInput('b'), 2);
            Assert.AreEqual("bbo", _textView.TextSnapshot.GetLineFromLineNumber(0).GetText());
        }

        [Test]
        public void ReplaceChar3()
        {
            Create("foo");
            _textView.MoveCaretTo(1);
            _commandUtil.ReplaceChar(KeyInputUtil.EnterKey, 1);
            var tss = _textView.TextSnapshot;
            Assert.AreEqual(2, tss.LineCount);
            Assert.AreEqual("f", tss.GetLineFromLineNumber(0).GetText());
            Assert.AreEqual("o", tss.GetLineFromLineNumber(1).GetText());
        }

        [Test]
        public void ReplaceChar4()
        {
            Create("food");
            _textView.MoveCaretTo(1);
            _commandUtil.ReplaceChar(KeyInputUtil.EnterKey, 2);
            var tss = _textView.TextSnapshot;
            Assert.AreEqual(2, tss.LineCount);
            Assert.AreEqual("f", tss.GetLineFromLineNumber(0).GetText());
            Assert.AreEqual("d", tss.GetLineFromLineNumber(1).GetText());
        }

        /// <summary>
        /// Should beep when the count exceeds the buffer length
        ///
        /// Unknown: Should the command still succeed though?  Choosing yes for now but could
        /// certainly be wrong about this.  Thinking yes though because there is no error message
        /// to display
        /// </summary>
        [Test]
        public void ReplaceChar_CountExceedsBufferLength()
        {
            Create("food");
            var tss = _textView.TextSnapshot;
            _operations.Setup(x => x.Beep()).Verifiable();
            Assert.IsTrue(_commandUtil.ReplaceChar(KeyInputUtil.CharToKeyInput('c'), 200).IsCompleted);
            Assert.AreSame(tss, _textView.TextSnapshot);
            _factory.Verify();
        }

        /// <summary>
        /// Caret should not move as a result of a single ReplaceChar operation
        /// </summary>
        [Test]
        public void ReplaceChar_DontMoveCaret()
        {
            Create("foo");
            Assert.IsTrue(_commandUtil.ReplaceChar(KeyInputUtil.CharToKeyInput('u'), 1).IsCompleted);
            Assert.AreEqual(0, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Caret should move for a multiple replace
        /// </summary>
        [Test]
        public void ReplaceChar_MoveCaretForMultiple()
        {
            Create("foo");
            Assert.IsTrue(_commandUtil.ReplaceChar(KeyInputUtil.CharToKeyInput('u'), 2).IsCompleted);
            Assert.AreEqual(1, _textView.GetCaretPoint().Position);
        }

        [Test]
        public void SetMarkToCaret_StartOfBuffer()
        {
            Create("the cat chased the dog");
            _operations.Setup(x => x.SetMark(_textView.GetCaretPoint(), 'a', _markMap)).Returns(Result.Succeeded).Verifiable();
            _commandUtil.SetMarkToCaret('a');
            _operations.Verify();
        }

        /// <summary>
        /// Beep and pass the error message onto IStatusUtil if there is na error
        /// </summary>
        [Test]
        public void SetMarkToCaret_BeepOnFailure()
        {
            Create("the cat chased the dog");
            _operations.Setup(x => x.SetMark(_textView.GetCaretPoint(), 'a', _markMap)).Returns(Result.NewFailed("e")).Verifiable();
            _operations.Setup(x => x.Beep()).Verifiable();
            _statusUtil.Setup(x => x.OnError("e")).Verifiable();
            _commandUtil.SetMarkToCaret('a');
            _factory.Verify();
        }

        [Test]
        public void JumpToMark_Simple()
        {
            Create("the cat chased the dog");
            _operations.Setup(x => x.JumpToMark('a', _markMap)).Returns(Result.Succeeded).Verifiable();
            _commandUtil.JumpToMark('a');
            _operations.Verify();
        }

        /// <summary>
        /// Pass the error message onto IStatusUtil if there is na error
        /// </summary>
        [Test]
        public void JumpToMark_OnFailure()
        {
            Create("the cat chased the dog");
            _operations.Setup(x => x.JumpToMark('a', _markMap)).Returns(Result.NewFailed("e")).Verifiable();
            _statusUtil.Setup(x => x.OnError("e")).Verifiable();
            _commandUtil.JumpToMark('a');
            _factory.Verify();
        }

        /// <summary>
        /// If there is no command to repeat then just beep
        /// </summary>
        [Test]
        public void RepeatLastCommand_NoCommandToRepeat()
        {
            Create("foo");
            _operations.Setup(x => x.Beep()).Verifiable();
            _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData());
            _factory.Verify();
        }

        /// <summary>
        /// Repeat a simple text insert
        /// </summary>
        [Test]
        public void RepeatLastCommand_InsertText()
        {
            Create("");
            _vimData.LastCommand = FSharpOption.Create(StoredCommand.NewTextChangeCommand(TextChange.NewInsert("h")));
            _operations.Setup(x => x.InsertText("h", 1)).Verifiable();
            _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData());
            _factory.Verify();
        }

        /// <summary>
        /// Repeat a simple text insert with a new count
        /// </summary>
        [Test]
        public void RepeatLastCommand_InsertTextNewCount()
        {
            Create("");
            _vimData.LastCommand = FSharpOption.Create(StoredCommand.NewTextChangeCommand(TextChange.NewInsert("h")));
            _operations.Setup(x => x.InsertText("h", 3)).Verifiable();
            _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData(count: 3));
            _factory.Verify();
        }

        /// <summary>
        /// Repeat a simple command
        /// </summary>
        [Test]
        public void RepeatLastCommand_SimpleCommand()
        {
            Create("");
            var didRun = false;
            SetLastCommand(VimUtil.CreatePing(data =>
            {
                Assert.IsTrue(data.Count.IsNone());
                Assert.IsTrue(data.RegisterName.IsNone());
                didRun = true;
            }));

            _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData());
            Assert.IsTrue(didRun);
        }

        /// <summary>
        /// Repeat a simple command but give it a new count.  This should override the previous
        /// count
        /// </summary>
        [Test]
        public void RepeatLastCommand_SimpleCommandNewCount()
        {
            Create("");
            var didRun = false;
            SetLastCommand(VimUtil.CreatePing(data =>
            {
                Assert.IsTrue(data.Count.IsSome(2));
                Assert.IsTrue(data.RegisterName.IsNone());
                didRun = true;
            }));

            _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData(count: 2));
            Assert.IsTrue(didRun);
        }

        /// <summary>
        /// Repeating a command should not clear the last command
        /// </summary>
        [Test]
        public void RepeatLastCommand_DontClearPrevious()
        {
            Create("");
            var didRun = false;
            var command = VimUtil.CreatePing(data =>
            {
                Assert.IsTrue(data.Count.IsNone());
                Assert.IsTrue(data.RegisterName.IsNone());
                didRun = true;
            });
            SetLastCommand(command);
            var saved = _vimData.LastCommand.Value;
            _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData());
            Assert.AreEqual(saved, _vimData.LastCommand.Value);
            Assert.IsTrue(didRun);
        }

        /// <summary>
        /// Guard against the possiblitity of creating a StackOverflow by having the repeat
        /// last command recursively call itself
        /// </summary>
        [Test]
        public void RepeatLastCommand_GuardAgainstStacOverflow()
        {
            var didRun = false;
            SetLastCommand(VimUtil.CreatePing(data =>
            {
                didRun = true;
                _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData());
            }));

            _statusUtil.Setup(x => x.OnError(Resources.NormalMode_RecursiveRepeatDetected)).Verifiable();
            _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData());
            _factory.Verify();
            Assert.IsTrue(didRun);
        }

        /// <summary>
        /// When dealing with a repeat of a linked command where a new count is provided, only
        /// the first command gets the new count.  The linked command gets the original count
        /// </summary>
        [Test]
        public void RepeatLastCommand_OnlyFirstCommandGetsNewCount()
        {
            Create("");
            var didRun1 = false;
            var didRun2 = false;
            var command1 = VimUtil.CreatePing(
                data =>
                {
                    didRun1 = true;
                    Assert.AreEqual(2, data.CountOrDefault);
                });
            var command2 = VimUtil.CreatePing(
                data =>
                {
                    didRun2 = true;
                    Assert.AreEqual(1, data.CountOrDefault);
                });
            var command = StoredCommand.NewLinkedCommand(
                StoredCommand.NewNormalCommand(command1, VimUtil.CreateCommandData(), CommandFlags.None),
                StoredCommand.NewNormalCommand(command2, VimUtil.CreateCommandData(), CommandFlags.None));
            _vimData.LastCommand = FSharpOption.Create(command);
            _commandUtil.RepeatLastCommand(VimUtil.CreateCommandData(count: 2));
            Assert.IsTrue(didRun1 && didRun2);
        }

        /// <summary>
        /// Pass a barrage of spans and verify they map back and forth within the same 
        /// ITextBuffer
        /// </summary>
        [Test]
        public void CalculateVisualSpan_CharacterBackAndForth()
        {
            Create("the dog kicked the ball", "into the tree");

            Action<SnapshotSpan> action = span =>
            {
                var visual = VisualSpan.NewCharacter(span);
                var stored = StoredVisualSpan.OfVisualSpan(visual);
                var restored = _commandUtil.CalculateVisualSpan(stored);
                Assert.AreEqual(visual, restored);
            };

            action(new SnapshotSpan(_textView.TextSnapshot, 0, 3));
            action(new SnapshotSpan(_textView.TextSnapshot, 0, 4));
            action(new SnapshotSpan(_textView.GetLine(0).Start, _textView.GetLine(1).Start.Add(1)));
        }

        /// <summary>
        /// When repeating a multi-line characterwise span where the caret moves left,
        /// we need to use the caret to the end of the line on the first line
        /// </summary>
        [Test]
        public void CalculateVisualSpan_CharacterMultilineMoveCaretLeft()
        {
            Create("the dog", "ball");

            var span = new SnapshotSpan(_textView.GetPoint(3), _textView.GetLine(1).Start.Add(1));
            var stored = StoredVisualSpan.OfVisualSpan(VisualSpan.NewCharacter(span));
            _textView.MoveCaretTo(1);
            var restored = _commandUtil.CalculateVisualSpan(stored);
            var expected = new SnapshotSpan(_textView.GetPoint(1), _textView.GetLine(1).Start.Add(1));
            Assert.AreEqual(expected, restored.AsCharacter().Item);
        }

        /// <summary>
        /// When restoring for a single line maintain the length but do it from the caret
        /// point and not the original
        /// </summary>
        [Test]
        public void CalculateVisualSpan_CharacterSingleLine()
        {
            Create("the dog kicked the cat", "and ball");

            var span = new SnapshotSpan(_textView.TextSnapshot, 3, 4);
            var stored = StoredVisualSpan.OfVisualSpan(VisualSpan.NewCharacter(span));
            _textView.MoveCaretTo(1);
            var restored = _commandUtil.CalculateVisualSpan(stored);
            var expected = new SnapshotSpan(_textView.GetPoint(1), 4);
            Assert.AreEqual(expected, restored.AsCharacter().Item);
        }

        /// <summary>
        /// Restore a Linewise span from the same offset
        /// </summary>
        [Test]
        public void CalculateVisualSpan_Linewise()
        {
            Create("a", "b", "c", "d");
            var span = VisualSpan.NewLine(_textView.GetLineRange(0, 1));
            var stored = StoredVisualSpan.OfVisualSpan(span);
            var restored = _commandUtil.CalculateVisualSpan(stored);
            Assert.AreEqual(span, restored);
        }

        /// <summary>
        /// Restore a Linewise span from a different offset
        /// </summary>
        [Test]
        public void CalculateVisualSpan_LinewiseDifferentOffset()
        {
            Create("a", "b", "c", "d");
            var span = VisualSpan.NewLine(_textView.GetLineRange(0, 1));
            var stored = StoredVisualSpan.OfVisualSpan(span);
            _textView.MoveCaretToLine(1);
            var restored = _commandUtil.CalculateVisualSpan(stored);
            Assert.AreEqual(_textView.GetLineRange(1, 2), restored.AsLine().Item);
        }

        /// <summary>
        /// Restore a Linewise span from a different offset which causes the count
        /// to be invalid
        /// </summary>
        [Test]
        public void CalculateVisualSpan_LinewiseCountPastEndOfBuffer()
        {
            Create("a", "b", "c", "d");
            var span = VisualSpan.NewLine(_textView.GetLineRange(0, 2));
            var stored = StoredVisualSpan.OfVisualSpan(span);
            _textView.MoveCaretToLine(3);
            var restored = _commandUtil.CalculateVisualSpan(stored);
            Assert.AreEqual(_textView.GetLineRange(3, 3), restored.AsLine().Item);
        }

        /// <summary>
        /// Restore of Block span at the same offset.  
        /// </summary>
        [Test]
        public void CalculateVisualSpan_Block()
        {
            Create("the", "dog", "kicked", "the", "ball");

            var col = _textView.GetBlock(0, 1, 0, 2);
            var span = VisualSpan.NewBlock(col);
            var stored = StoredVisualSpan.OfVisualSpan(span);
            var restored = _commandUtil.CalculateVisualSpan(stored);
            CollectionAssert.AreEquivalent(col, restored.AsBlock().Item);
        }

        /// <summary>
        /// Restore of Block span at one character to the right
        /// </summary>
        [Test]
        public void CalculateVisualSpan_BlockOneCharecterRight()
        {
            Create("the", "dog", "kicked", "the", "ball");

            var col = _textView.GetBlock(0, 1, 0, 2);
            var span = VisualSpan.NewBlock(col);
            var stored = StoredVisualSpan.OfVisualSpan(span);
            _textView.MoveCaretTo(1);
            var restored = _commandUtil.CalculateVisualSpan(stored);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    _textView.GetLineSpan(0, 1, 1),
                    _textView.GetLineSpan(1, 1, 1)
                },
                restored.AsBlock().Item);
        }

        [Test]
        public void DeleteCharacterAtCaret_Simple()
        {
            Create("foo", "bar");
            _commandUtil.DeleteCharacterAtCaret(1, UnnamedRegister);
            Assert.AreEqual("oo", _textView.GetLine(0).GetText());
            Assert.AreEqual("f", UnnamedRegister.StringValue);
            Assert.AreEqual(0, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Delete several characters
        /// </summary>
        [Test]
        public void DeleteCharacterAtCaret_TwoCharacters()
        {
            Create("foo", "bar");
            _commandUtil.DeleteCharacterAtCaret(2, UnnamedRegister);
            Assert.AreEqual("o", _textView.GetLine(0).GetText());
            Assert.AreEqual("fo", UnnamedRegister.StringValue);
        }

        /// <summary>
        /// Delete at a different offset and make sure the cursor is positioned correctly
        /// </summary>
        [Test]
        public void DeleteCharacterAtCaret_NonZeroOffset()
        {
            Create("the cat", "bar");
            _textView.MoveCaretTo(1);
            _commandUtil.DeleteCharacterAtCaret(2, UnnamedRegister);
            Assert.AreEqual("t cat", _textView.GetLine(0).GetText());
            Assert.AreEqual("he", UnnamedRegister.StringValue);
            Assert.AreEqual(1, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// When the count exceeds the length of the line it should delete to the end of the 
        /// line
        /// </summary>
        [Test]
        public void DeleteCharacterAtCaret_CountExceedsLine()
        {
            Create("the cat", "bar");
            _textView.MoveCaretTo(1);
            _commandUtil.DeleteCharacterAtCaret(300, UnnamedRegister);
            Assert.AreEqual("t", _textView.GetLine(0).GetText());
            Assert.AreEqual("he cat", UnnamedRegister.StringValue);
            Assert.AreEqual(1, _textView.GetCaretPoint().Position);
        }

        [Test]
        public void DeleteCharacterBeforeCaret_Simple()
        {
            Create("foo");
            _textView.MoveCaretTo(1);
            _commandUtil.DeleteCharacterBeforeCaret(1, UnnamedRegister);
            Assert.AreEqual("oo", _textView.GetLine(0).GetText());
            Assert.AreEqual("f", UnnamedRegister.StringValue);
            Assert.AreEqual(0, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// When the count exceeds the line just delete to the start of the line
        /// </summary>
        [Test]
        public void DeleteCharacterBeforeCaret_CountExceedsLine()
        {
            Create("foo");
            _textView.MoveCaretTo(1);
            _commandUtil.DeleteCharacterBeforeCaret(300, UnnamedRegister);
            Assert.AreEqual("oo", _textView.GetLine(0).GetText());
            Assert.AreEqual("f", UnnamedRegister.StringValue);
            Assert.AreEqual(0, _textView.GetCaretPoint().Position);
        }

        [Test]
        public void DeleteLinesIncludingLineBreak_Simple()
        {
            Create("foo", "bar", "baz", "jaz");
            _commandUtil.DeleteLines(1, UnnamedRegister);
            Assert.AreEqual("bar", _textView.GetLine(0).GetText());
            Assert.AreEqual("foo" + Environment.NewLine, UnnamedRegister.StringValue);
            Assert.AreEqual(0, _textView.GetCaretPoint().Position);
        }

        [Test]
        public void DeleteLinesIncludingLineBreak_WithCount()
        {
            Create("foo", "bar", "baz", "jaz");
            _commandUtil.DeleteLines(2, UnnamedRegister);
            Assert.AreEqual("baz", _textView.GetLine(0).GetText());
            Assert.AreEqual("foo" + Environment.NewLine + "bar" + Environment.NewLine, UnnamedRegister.StringValue);
            Assert.AreEqual(0, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Delete the last line and make sure it actually deletes a line from the buffer
        /// </summary>
        [Test]
        public void DeleteLinesIncludingLineBreak_LastLine()
        {
            Create("foo", "bar");
            _textView.MoveCaretToLine(1);
            _commandUtil.DeleteLines(1, UnnamedRegister);
            Assert.AreEqual("bar" + Environment.NewLine, UnnamedRegister.StringValue);
            Assert.AreEqual(1, _textView.TextSnapshot.LineCount);
            Assert.AreEqual("foo", _textView.GetLine(0).GetText());
        }

        /// <summary>
        /// Caret should be moved to the start of the shift
        /// </summary>
        [Test]
        public void ShiftLinesRightVisual_BlockShouldPutCaretAtStart()
        {
            Create("cat", "dog");
            _textView.MoveCaretToLine(1);
            var span = _textView.GetVisualSpanBlock(column: 1, length: 2, startLine: 0, lineCount: 2);
            _operations
                .Setup(x => x.ShiftLineBlockRight(span.AsBlock().item, 1))
                .Callback(() => _textView.SetText("c  at", "d  og"))
                .Verifiable();
            _commandUtil.ShiftLinesRightVisual(1, span);
            Assert.AreEqual(1, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Caret should be moved to the start of the shift
        /// </summary>
        [Test]
        public void ShiftLinesLeftVisual_BlockShouldPutCaretAtStart()
        {
            Create("c  at", "d  og");
            _textView.MoveCaretToLine(1);
            var span = _textView.GetVisualSpanBlock(column: 1, length: 1, startLine: 0, lineCount: 2);
            _operations
                .Setup(x => x.ShiftLineBlockRight(span.AsBlock().item, 1))
                .Callback(() => _textView.SetText("cat", "dog"))
                .Verifiable();
            _commandUtil.ShiftLinesLeftVisual(1, span);
            Assert.AreEqual(1, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Changing a word based motion forward should not delete trailing whitespace
        /// </summary>
        [Test]
        public void ChangeMotion_WordSpan()
        {
            Create("foo  bar");
            _commandUtil.ChangeMotion(
                UnnamedRegister,
                VimUtil.CreateMotionResult(
                    _textBuffer.GetSpan(0, 3),
                    isForward: true,
                    isAnyWord: true,
                    motionKind: MotionKind.Inclusive,
                    operationKind: OperationKind.CharacterWise));
            Assert.AreEqual("  bar", _textBuffer.GetLineRange(0).GetText());
            Assert.AreEqual("foo", UnnamedRegister.StringValue);
        }

        /// <summary>
        /// Changing a word based motion forward should not delete trailing whitespace
        /// </summary>
        [Test]
        public void ChangeMotion_WordShouldSaveTrailingWhitespace()
        {
            Create("foo  bar");
            _commandUtil.ChangeMotion(
                UnnamedRegister,
                VimUtil.CreateMotionResult(
                    _textBuffer.GetSpan(0, 5),
                    isForward: true,
                    isAnyWord: true,
                    motionKind: MotionKind.Inclusive,
                    operationKind: OperationKind.LineWise));
            Assert.AreEqual("  bar", _textBuffer.GetLineRange(0).GetText());
            Assert.AreEqual("foo", UnnamedRegister.StringValue);
            Assert.AreEqual(0, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Delete trailing whitespace in a non-word motion
        /// </summary>
        [Test]
        public void ChangeMotion_NonWordShouldDeleteTrailingWhitespace()
        {
            Create("foo  bar");
            _commandUtil.ChangeMotion(
                UnnamedRegister,
                VimUtil.CreateMotionResult(
                    _textBuffer.GetSpan(0, 5),
                    isForward: true,
                    isAnyWord: false,
                    motionKind: MotionKind.Inclusive,
                    operationKind: OperationKind.LineWise));
            Assert.AreEqual("bar", _textBuffer.GetLineRange(0).GetText());
        }

        /// <summary>
        /// Leave whitespace in a backward word motion
        /// </summary>
        [Test]
        public void ChangeMotion_LeaveWhitespaceIfBackward()
        {
            Create("cat dog tree");
            _commandUtil.ChangeMotion(
                UnnamedRegister,
                VimUtil.CreateMotionResult(
                    _textBuffer.GetSpan(4, 4),
                    false,
                    MotionKind.Inclusive,
                    OperationKind.CharacterWise));
            Assert.AreEqual("cat tree", _textBuffer.GetLineRange(0).GetText());
            Assert.AreEqual(4, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Caret should be positioned at the end of the first line
        /// </summary>
        [Test]
        public void JoinLines_Caret()
        {
            Create("dog", "cat", "bear");
            _operations
                .Setup(x => x.Join(_textView.GetLineRange(0, 1), JoinKind.RemoveEmptySpaces))
                .Callback(() => _textView.SetText("dog cat", "bear"))
                .Verifiable();
            _commandUtil.JoinLines(JoinKind.RemoveEmptySpaces, 1);
            _operations.Verify();
            Assert.AreEqual(3, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Should beep when the count specified causes the range to exceed the 
        /// length of the ITextBuffer
        /// </summary>
        [Test]
        public void JoinLines_CountExceedsBuffer()
        {
            Create("dog", "cat", "bear");
            _operations.Setup(x => x.Beep()).Verifiable();
            _commandUtil.JoinLines(JoinKind.RemoveEmptySpaces, 3000);
            _operations.Verify();
        }

        /// <summary>
        /// A count of 2 is the same as 1 for JoinLines
        /// </summary>
        [Test]
        public void JoinLines_CountOfTwoIsSameAsOne()
        {
            Create("dog", "cat", "bear");
            _operations
                .Setup(x => x.Join(_textView.GetLineRange(0, 1), JoinKind.RemoveEmptySpaces))
                .Callback(() => _textView.SetText("dog cat", "bear"))
                .Verifiable();
            _commandUtil.JoinLines(JoinKind.RemoveEmptySpaces, 2);
            _operations.Verify();
            Assert.AreEqual(3, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// The caret behavior for the 'J' family of commands is hard to follow at first
        /// but comes down to a very simple behavior.  The caret should be placed 1 past
        /// the last character in the second to last line joined
        /// </summary>
        [Test]
        public void JoinLines_CaretWithBlankAtEnd()
        {
            Create("a ", "b", "c");
            _operations
                .Setup(x => x.Join(_textView.GetLineRange(0, 2), JoinKind.RemoveEmptySpaces))
                .Callback(() => _textView.SetText("a b c"))
                .Verifiable();
            _commandUtil.JoinLines(JoinKind.RemoveEmptySpaces, 3);
            _operations.Verify();
            Assert.AreEqual(3, _textView.GetCaretPoint().Position);
        }

        [Test]
        public void ChangeCaseCaretPoint_Simple()
        {
            Create("bar", "baz");
            _commandUtil.ChangeCaseCaretPoint(ChangeCharacterKind.ToUpperCase, 1);
            Assert.AreEqual("Bar", _textView.GetLineRange(0).GetText());
            Assert.AreEqual(1, _textView.GetCaretPoint().Position);
        }

        [Test]
        public void ChangeCaseCaretPoint_WithCount()
        {
            Create("bar", "baz");
            _commandUtil.ChangeCaseCaretPoint(ChangeCharacterKind.ToUpperCase, 2);
            Assert.AreEqual("BAr", _textView.GetLineRange(0).GetText());
            Assert.AreEqual(2, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// If the count exceeds the line then just do the rest of the line
        /// </summary>
        [Test]
        public void ChangeCaseCaretPoint_CountExceedsLine()
        {
            Create("bar", "baz");
            _commandUtil.ChangeCaseCaretPoint(ChangeCharacterKind.ToUpperCase, 300);
            Assert.AreEqual("BAR", _textView.GetLine(0).GetText());
            Assert.AreEqual("baz", _textView.GetLine(1).GetText());
            Assert.AreEqual(3, _textView.GetCaretPoint().Position);
        }

        [Test]
        public void ChangeCaseCaretLine_Simple()
        {
            Create("foo", "bar");
            _textView.MoveCaretTo(1);
            _commandUtil.ChangeCaseCaretLine(ChangeCharacterKind.ToUpperCase);
            Assert.AreEqual("FOO", _textView.GetLine(0).GetText());
            Assert.AreEqual(0, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Make sure the caret moves past the whitespace when changing case
        /// </summary>
        [Test]
        public void ChangeCaseCaretLine_WhiteSpaceStart()
        {
            Create("  foo", "bar");
            _textView.MoveCaretTo(4);
            _commandUtil.ChangeCaseCaretLine(ChangeCharacterKind.ToUpperCase);
            Assert.AreEqual("  FOO", _textView.GetLine(0).GetText());
            Assert.AreEqual(2, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Don't change anything but letters
        /// </summary>
        [Test]
        public void ChangeCaseCaretLine_ExcludeNumbers()
        {
            Create("foo123", "bar");
            _textView.MoveCaretTo(1);
            _commandUtil.ChangeCaseCaretLine(ChangeCharacterKind.ToUpperCase);
            Assert.AreEqual("FOO123", _textView.GetLine(0).GetText());
            Assert.AreEqual(0, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Change the caret line with the rot13 encoding
        /// </summary>
        [Test]
        public void ChangeCaseCaretLine_Rot13()
        {
            Create("hello", "bar");
            _textView.MoveCaretTo(1);
            _commandUtil.ChangeCaseCaretLine(ChangeCharacterKind.Rot13);
            Assert.AreEqual("uryyb", _textView.GetLine(0).GetText());
            Assert.AreEqual(0, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// An invalid motion should produce an error and not call the pased in function
        /// </summary>
        [Test]
        public void RunWithMotion_InvalidMotionShouldError()
        {
            Create("");
            var data = VimUtil.CreateMotionData(Motion.NewMark('a'));
            Func<MotionResult, CommandResult> func =
                _ =>
                {
                    Assert.Fail("Should not run");
                    return null;
                };
            _statusUtil.Setup(x => x.OnError(Resources.MotionCapture_InvalidMotion)).Verifiable();
            var result = _commandUtil.RunWithMotion(data, func.ToFSharpFunc());
            Assert.IsTrue(result.IsError);
            _factory.Verify();
        }

        /// <summary>
        /// Replace the text and put the caret at the end of the selection
        /// </summary>
        [Test]
        public void PutOverSelection_Character()
        {
            Create("hello world");
            var visualSpan = VisualSpan.NewCharacter(_textView.GetLineSpan(0, 0, 5));
            UnnamedRegister.UpdateValue("dog");
            _commandUtil.PutOverSelection(UnnamedRegister, 1, visualSpan, moveCaretAfterText: false);
            Assert.AreEqual("dog world", _textView.GetLine(0).GetText());
            Assert.AreEqual(2, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Replace the text and put the caret after the selection span
        /// </summary>
        [Test]
        public void PutOverSelection_Character_WithCaretMove()
        {
            Create("hello world");
            var visualSpan = VisualSpan.NewCharacter(_textView.GetLineSpan(0, 0, 5));
            UnnamedRegister.UpdateValue("dog");
            _commandUtil.PutOverSelection(UnnamedRegister, 1, visualSpan, moveCaretAfterText: true);
            Assert.AreEqual("dog world", _textView.GetLine(0).GetText());
            Assert.AreEqual(3, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Make sure it removes both lines and inserts the text at the start 
        /// of the line range span.  Should position the caret at the start as well
        /// </summary>
        [Test]
        public void PutOverSelection_Line()
        {
            Create("the cat", "chased", "the dog");
            var visualSpan = VisualSpan.NewLine(_textView.GetLineRange(0, 1));
            UnnamedRegister.UpdateValue("dog");
            _commandUtil.PutOverSelection(UnnamedRegister, 1, visualSpan, moveCaretAfterText: false);
            Assert.AreEqual("dog", _textView.GetLine(0).GetText());
            Assert.AreEqual("the dog", _textView.GetLine(1).GetText());
            Assert.AreEqual(0, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Caret should be moved to the start of the next line if the 'moveCaretAfterText' 
        /// option is specified
        /// </summary>
        [Test]
        public void PutOverSelection_Line_WithCaretMove()
        {
            Create("the cat", "chased", "the dog");
            var visualSpan = VisualSpan.NewLine(_textView.GetLineRange(0, 1));
            UnnamedRegister.UpdateValue("dog");
            _commandUtil.PutOverSelection(UnnamedRegister, 1, visualSpan, moveCaretAfterText: true);
            Assert.AreEqual("dog", _textView.GetLine(0).GetText());
            Assert.AreEqual("the dog", _textView.GetLine(1).GetText());
            Assert.AreEqual(_textView.GetLine(1).Start, _textView.GetCaretPoint());
        }

        [Test]
        public void PutOverSelection_Block()
        {
            Create("cat", "dog", "bear", "fish");
            var visualSpan = VisualSpan.NewBlock(_textView.GetBlock(1, 1, 0, 2));
            UnnamedRegister.UpdateValue("z");
            _commandUtil.PutOverSelection(UnnamedRegister, 1, visualSpan, moveCaretAfterText: false);
            Assert.AreEqual("czt", _textView.GetLine(0).GetText());
            Assert.AreEqual("dg", _textView.GetLine(1).GetText());
            Assert.AreEqual(1, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// Should delete the entire line range encompasing the selection and position the 
        /// caret at the start of the range for undo / redo
        /// </summary>
        [Test]
        public void DeleteLineSelection_Character()
        {
            Create("cat", "dog");
            var visualSpan = VisualSpan.NewCharacter(_textView.GetLineSpan(0, 1, 1));
            _operations.Setup(x => x.MoveCaretForVirtualEdit());
            _commandUtil.DeleteLineSelection(UnnamedRegister, visualSpan);
            Assert.AreEqual("cat" + Environment.NewLine, UnnamedRegister.StringValue);
            Assert.AreEqual("dog", _textView.GetLine(0).GetText());
            Assert.AreEqual(1, _textView.GetCaretPoint().Position);
            _operations.Verify();
        }

        /// <summary>
        /// Should delete the entire line range encompasing the selection and position the 
        /// caret at the start of the range for undo / redo
        /// </summary>
        [Test]
        public void DeleteLineSelection_Line()
        {
            Create("cat", "dog");
            var visualSpan = VisualSpan.NewLine(_textView.GetLineRange(0));
            _commandUtil.DeleteLineSelection(UnnamedRegister, visualSpan);
            Assert.AreEqual("cat" + Environment.NewLine, UnnamedRegister.StringValue);
            Assert.AreEqual("dog", _textView.GetLine(0).GetText());
            Assert.AreEqual(0, _textView.GetCaretPoint().Position);
        }

        /// <summary>
        /// When deleting a block it should delete from the start of the span until the end
        /// of the line for every span.  Caret should be positioned at the start of the edit
        /// but backed off a single space due to 'virtualedit='.  This will be properly
        /// handled by the moveCaretForVirtualEdit function.  Ensure it's called
        /// </summary>
        [Test]
        public void DeleteLineSelection_Block()
        {
            Create("cat", "dog", "fish");
            _globalSettings.VirtualEdit = String.Empty;
            _operations.Setup(x => x.MoveCaretForVirtualEdit());
            var visualSpan = VisualSpan.NewBlock(_textView.GetBlock(1, 1, 0, 2));
            _commandUtil.DeleteLineSelection(UnnamedRegister, visualSpan);
            Assert.AreEqual("c", _textView.GetLine(0).GetText());
            Assert.AreEqual("d", _textView.GetLine(1).GetText());
            Assert.AreEqual(1, _textView.GetCaretPoint().Position);
            _operations.Verify();
        }
    }
}