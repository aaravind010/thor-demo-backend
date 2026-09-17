namespace Thor.Authorizer.Core.Policy;

public interface IPolicyBuilder
{
    PolicyDocument Build(AuthResult authResult, string methodArn);
}
