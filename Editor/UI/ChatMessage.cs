namespace BurgerMonster.ClaudeAgent
{
    public enum ChatRole { User, Assistant, System }

    public class ChatMessage
    {
        public ChatRole Role;
        public string Text;

        public ChatMessage(ChatRole role, string text)
        {
            Role = role;
            Text = text;
        }
    }
}
