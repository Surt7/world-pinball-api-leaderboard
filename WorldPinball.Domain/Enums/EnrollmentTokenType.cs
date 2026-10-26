namespace WorldPinball.Domain.Enums
{
    public enum EnrollmentTokenType { DeviceEnroll, DeviceAttach, DeviceRotateKey, AdminBootstrap,TerminalBootstrap  }
    public enum CaType { Root, Intermediate }

    public enum DeviceCertificateStatus { Active, Expired, Revoked, Superseded }

    public enum RevocationReason
    {
        KeyCompromise,
        DeviceCompromise,
        AffiliationChanged,
        CessationOfOperation,
        Superseded,
        Unspecified
    }
}
