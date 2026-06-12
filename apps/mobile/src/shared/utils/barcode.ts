export type BarcodeFormat = "EAN13" | "CODE128";

export function calculateEAN13CheckDigit(firstTwelveDigits: string): number | null {
  if (!/^\d{12}$/.test(firstTwelveDigits)) {
    return null;
  }

  const digits = firstTwelveDigits.split("").map(Number);
  const sum = digits.reduce((total, digit, index) => {
    const position = index + 1;
    return total + digit * (position % 2 === 0 ? 3 : 1);
  }, 0);

  return (10 - (sum % 10)) % 10;
}

export function isValidEAN13(barcode: string): boolean {
  if (!/^\d{13}$/.test(barcode)) {
    return false;
  }

  const expectedCheckDigit = calculateEAN13CheckDigit(barcode.slice(0, 12));
  if (expectedCheckDigit === null) {
    return false;
  }

  return expectedCheckDigit === Number(barcode[12]);
}

export function resolveBarcodeFormat(barcode: string): BarcodeFormat {
  return isValidEAN13(barcode) ? "EAN13" : "CODE128";
}
