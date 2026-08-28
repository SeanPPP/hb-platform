import * as iconv from "iconv-lite";

const GB18030 = "gb18030";
const FALLBACK_BYTE = 0x3f;

/** 初始化 ESC/POS，并显式进入中文字符模式。 */
export function appendEscPosInitialize(output: number[]): void {
  output.push(0x1b, 0x40, 0x1c, 0x26);
}

/**
 * 将票面文本编码为 N160 内置中文字库可稳定处理的 GB18030 字节。
 * 四字节映射和无法往返的字符统一降级为问号，避免罕见字形中断收银打印。
 */
export function encodeEscPosText(value: string): Uint8Array {
  const output: number[] = [];
  for (const character of value) {
    const encoded = iconv.encode(character, GB18030);
    if (
      encoded.length <= 2 &&
      iconv.decode(encoded, GB18030) === character
    ) {
      output.push(...encoded);
    } else {
      output.push(FALLBACK_BYTE);
    }
  }
  return Uint8Array.from(output);
}
