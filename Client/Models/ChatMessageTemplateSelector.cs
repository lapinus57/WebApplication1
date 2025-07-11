using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace Client.Models
{
    public class ChatMessageTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? MyMessageTemplate { get; set; }
        public DataTemplate? OtherMessageTemplate { get; set; }
        public DataTemplate? MyColoredTemplate { get; set; }
        public DataTemplate? OtherColoredTemplate { get; set; }
        public DataTemplate? LoadMoreTemplate { get; set; }
        public DataTemplate? EmptyTemplate { get; set; }
        public string MyUsername { get; set; } = string.Empty;
        public DataTemplate? IrcTemplate { get; set; }

        public ChatStyle DisplayMode { get; set; } = ChatStyle.Modern;
        public bool UseSenderColor { get; set; }

        protected override DataTemplate? SelectTemplateCore(object item)
        {
            if (item is LoadMorePlaceholder)
                return LoadMoreTemplate;

            if (item is EmptyPlaceholder)
                return EmptyTemplate;

            if (item is ChatMessageModel message)
            {
                Debug.WriteLine($"[Selector] Style={DisplayMode}, Sender={message.Sender}, MyUsername={MyUsername}, UseSenderColor={UseSenderColor}");
                if (DisplayMode == ChatStyle.OldSchool)
                    return IrcTemplate;
                else if (UseSenderColor)
                    return message.Sender == MyUsername ? MyColoredTemplate : OtherColoredTemplate;
                else
                    return message.Sender == MyUsername ? MyMessageTemplate : OtherMessageTemplate;
            }

            return base.SelectTemplateCore(item);
        }
    }
}
