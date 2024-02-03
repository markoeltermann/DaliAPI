namespace UserInterface.Services
{
    public class Sender(HttpClient httpClient)
    {
        public void Send(string address, string action, string direction)
        {
            var url = $"DaliCommand/Switch/{address}?action={action}&direction={direction}";
            httpClient.GetAsync(url);
        }
    }
}