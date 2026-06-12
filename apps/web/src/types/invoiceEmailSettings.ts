export interface InvoiceEmailSettingsDto {
  host: string
  port: number
  useSsl: boolean
  checkCertificateRevocation: boolean
  username: string
  hasPassword: boolean
  fromEmail: string
  fromName: string
  maxAttachmentBytes: number
}

export interface InvoiceEmailSettingsSaveRequest {
  host: string
  port: number
  useSsl: boolean
  checkCertificateRevocation: boolean
  username: string
  password?: string
  clearPassword: boolean
  fromEmail: string
  fromName: string
  maxAttachmentBytes: number
}

export interface InvoiceEmailSettingsTestRequest extends InvoiceEmailSettingsSaveRequest {
  testToEmail: string
}

export interface InvoiceEmailSettingsTestResult {
  success: boolean
  message?: string
}
