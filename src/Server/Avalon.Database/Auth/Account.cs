namespace Avalon.Database.Auth
{
    public class Account
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public byte[] Salt { get; set; }
        public byte[] Verifier { get; set; }
    }
}
