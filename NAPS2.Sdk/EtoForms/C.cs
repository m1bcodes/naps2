using System;
using System.Diagnostics;
using System.Windows.Input;
using Eto.Drawing;
using Eto.Forms;

namespace NAPS2.EtoForms
{
    public static class C
    {
        /// <summary>
        /// Creates a label with wrapping disabled. For WinForms support, all labels must not wrap.
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static Label NoWrap(string text) =>
            new Label { Text = text, Wrap = WrapMode.None };

        /// <summary>
        /// Creates a link button with the specified text and action.
        /// If the action is not specified, it will assume the text is a URL to be opened.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="onClick"></param>
        /// <returns></returns>
        public static LinkButton Link(string text, Action onClick = null)
        {
            onClick ??= () => Process.Start(text);
            return new LinkButton
            {
                Text = text,
                Command = new ActionCommand(onClick)
            };
        }

        /// <summary>
        /// Creates a button with the specified text and action.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="onClick"></param>
        /// <returns></returns>
        public static Button Button(string text, Action onClick) =>
            new Button
            {
                Text = text,
                Command = new ActionCommand(onClick)
            };

        /// <summary>
        /// Creates a null placeholder for Eto layouts.
        /// 
        /// For example, it can be added to a row or column to absorb scaling.
        /// If it isn't at the end of the row/column, it must be annotated with .XScale() or .YScale(). 
        /// </summary>
        /// <returns></returns>
        public static ControlWithLayoutAttributes ZeroSpace() =>
            new ControlWithLayoutAttributes(null);

        /// <summary>
        /// Creates an label of default height to be used as a visual paragraph separator.
        /// </summary>
        /// <returns></returns>
        public static LayoutElement TextSpace() => NoWrap(" ");

        /// <summary>
        /// Creates a hacky image button that supports accessible interaction.
        /// 
        /// It works by overlaying an image on top a button.
        /// If the image has transparency an offset may need to be specified to keep the button hidden.
        /// If the text is too large relative to the button it will be impossible to hide fully.
        /// </summary>
        /// <param name="image"></param>
        /// <param name="text"></param>
        /// <param name="onClick"></param>
        /// <param name="xOffset"></param>
        /// <param name="yOffset"></param>
        /// <returns></returns>
        public static Control AccessibleImageButton(Image image, string text, Action onClick, int xOffset = 0, int yOffset = 0)
        {
            var imageView = new ImageView { Image = image, Cursor = Cursors.Pointer, };
            imageView.MouseDown += (sender, args) => onClick();
            var button = new Button
            {
                Text = text,
                Width = 0,
                Height = 0,
                Command = new ActionCommand(onClick)
            };
            var pix = new PixelLayout();
            pix.Add(button, xOffset, yOffset);
            pix.Add(imageView, 0, 0);
            return pix;
        }

        private class ActionCommand : ICommand
        {
            private readonly Action _action;

            public ActionCommand(Action action)
            {
                _action = action;
            }

            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter) => _action();

            public event EventHandler CanExecuteChanged;
        }
    }
}