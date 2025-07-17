using EasyNetQ;
using Newtonsoft.Json;

namespace reciveAccesos
{
    [Queue("AccesosResultadosCRM", ExchangeName = "clsQMessage.AccesosResultadosCRM, clsQMessage")]
    public class Message
    {
        public string Uuid { get; set; }
        public bool isEntrance { get; set; }
        public string date { get; set; }
        public bool isBeneficiario { get; set; }
        public ResourceData Resource { get; set; } = new ResourceData();
        public AccessPointData AccessPoint { get; set; } = new AccessPointData();
        public CredentialData Credential { get; set; } = new CredentialData { Beneficiary = new BeneficiaryData() };
    }

    public class ResourceData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ExternalId { get; set; }
        public string? StartHour { get; set; }
        public string? EndHour { get; set; }
    }

    public class AccessPointData
    {
        public string Id { get; set; }
        public int Operation { get; set; }
        public int Mode { get; set; }
        public bool active { get; set; }
        public string tenantId { get; set; }
    }

    public class CredentialData
    {
        public string Id { get; set; }
        public BeneficiaryData Beneficiary { get; set; }
    }

    public class BeneficiaryData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ExternalId { get; set; }
    }
}
