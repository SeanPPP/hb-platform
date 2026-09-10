let printing = false;

export class PersonalCodePrintBusyError extends Error {
  constructor() {
    super("PERSONAL_CODE_PRINT_BUSY");
    this.name = "PersonalCodePrintBusyError";
  }
}

export async function runPersonalCodePrintExclusive<T>(operation: () => Promise<T>): Promise<T> {
  // 本人码、员工单人和批量打印共用互斥；拒绝并发请求，不暗中排队重复出纸。
  if (printing) throw new PersonalCodePrintBusyError();
  printing = true;
  try {
    return await operation();
  } finally {
    printing = false;
  }
}
