import type {
  InvoiceEmailSettingsDto,
  InvoiceEmailSettingsSaveRequest,
  InvoiceEmailSettingsTestRequest,
} from '../../../types/invoiceEmailSettings'

export interface InvoiceEmailSettingsFormValues {
  host: string
  port: number
  useSsl: boolean
  checkCertificateRevocation: boolean
  username: string
  password: string
  clearPassword: boolean
  fromEmail: string
  fromName: string
  maxAttachmentBytes: number
  testToEmail: string
}

function normalizeCommonPayload(values: InvoiceEmailSettingsFormValues) {
  const trimmedPassword = values.password.trim()

  const payload: InvoiceEmailSettingsSaveRequest = {
    host: values.host.trim(),
    port: values.port,
    useSsl: values.useSsl,
    checkCertificateRevocation: values.checkCertificateRevocation,
    username: values.username.trim(),
    clearPassword: values.clearPassword,
    fromEmail: values.fromEmail.trim(),
    fromName: values.fromName.trim(),
    maxAttachmentBytes: values.maxAttachmentBytes,
  }

  // 后端约定 clearPassword=true 时只清空原密码，不接受新密码。
  if (!values.clearPassword && trimmedPassword) {
    payload.password = trimmedPassword
  }

  return payload
}

export function createInvoiceEmailSettingsFormValues(settings: InvoiceEmailSettingsDto): InvoiceEmailSettingsFormValues {
  return {
    host: settings.host,
    port: settings.port,
    useSsl: settings.useSsl,
    checkCertificateRevocation: settings.checkCertificateRevocation,
    username: settings.username,
    password: '',
    clearPassword: false,
    fromEmail: settings.fromEmail,
    fromName: settings.fromName,
    maxAttachmentBytes: settings.maxAttachmentBytes,
    testToEmail: '',
  }
}

export function buildInvoiceEmailSettingsSavePayload(
  values: InvoiceEmailSettingsFormValues,
): InvoiceEmailSettingsSaveRequest {
  return normalizeCommonPayload(values)
}

export function buildInvoiceEmailSettingsTestPayload(
  values: InvoiceEmailSettingsFormValues,
): InvoiceEmailSettingsTestRequest {
  return {
    ...normalizeCommonPayload(values),
    testToEmail: values.testToEmail.trim(),
  }
}
