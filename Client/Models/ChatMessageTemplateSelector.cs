using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Client.Models
{
    public enum ChatStyle
    {
        Modern,
        OldSchool
    }

    public class ChatMessageTemplateSelector : DataTemplateSelector
    {
        public string MyUsername { get; set; } = string.Empty;
        public DataTemplate? MyMessageTemplate { get; set; }
        public DataTemplate? OtherMessageTemplate { get; set; }
        public DataTemplate? IrcTemplate { get; set; }
        public DataTemplate? LoadMoreTemplate { get; set; }
        public DataTemplate? EmptyTemplate { get; set; }

        public ChatStyle DisplayMode { get; set; } = ChatStyle.Modern;

        protected override DataTemplate? SelectTemplateCore(object item)
        {
            if (item is LoadMorePlaceholder) return LoadMoreTemplate;
            if (item is EmptyPlaceholder) return EmptyTemplate;
            if (item is ChatMessageModel message)
            {
                if (!string.IsNullOrEmpty(message.IrcHeader))
                    return IrcTemplate;
                if (message.Header == MyUsername)
                    return MyMessageTemplate;
                return OtherMessageTemplate;
            }
            return base.SelectTemplateCore(item);
        }
    }
}
