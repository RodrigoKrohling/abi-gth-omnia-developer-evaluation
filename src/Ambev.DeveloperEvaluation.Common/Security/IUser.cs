namespace Ambev.DeveloperEvaluation.Common.Security
{
    /// <summary>
    /// Define o contrato para representação de um usuário no sistema.
    /// </summary>
    public interface IUser
    {
        /// <summary>
        /// Obtém o identificador único do usuário.
        /// </summary>
        /// <returns>O ID do usuário como uma string.</returns>
        public string Id { get; }

        /// <summary>
        /// Obtém o nome de usuário.
        /// </summary>
        /// <returns>O nome de usuário.</returns>
        public string Username { get; }

        /// <summary>
        /// Obtém o endereço de e-mail do usuário.
        /// </summary>
        /// <returns>O e-mail do usuário.</returns>
        /// <remarks>Written into the token as the Email claim, which BaseController reads.</remarks>
        public string Email { get; }

        /// <summary>
        /// Obtém o papel/função do usuário no sistema.
        /// </summary>
        /// <returns>O papel do usuário como uma string.</returns>
        public string Role { get; }
    }
}
