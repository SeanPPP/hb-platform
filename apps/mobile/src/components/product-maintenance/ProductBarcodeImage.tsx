import { StyleSheet, View } from "react-native";
import { Text } from "react-native-paper";
import { resolveBarcodeFormat } from "@/shared/utils/barcode";

const EAN13_LEFT_ODD = [
  "0001101",
  "0011001",
  "0010011",
  "0111101",
  "0100011",
  "0110001",
  "0101111",
  "0111011",
  "0110111",
  "0001011",
];

const EAN13_LEFT_EVEN = [
  "0100111",
  "0110011",
  "0011011",
  "0100001",
  "0011101",
  "0111001",
  "0000101",
  "0010001",
  "0001001",
  "0010111",
];

const EAN13_RIGHT = [
  "1110010",
  "1100110",
  "1101100",
  "1000010",
  "1011100",
  "1001110",
  "1010000",
  "1000100",
  "1001000",
  "1110100",
];

const EAN13_PARITY = [
  "LLLLLL",
  "LLGLGG",
  "LLGGLG",
  "LLGGGL",
  "LGLLGG",
  "LGGLLG",
  "LGGGLL",
  "LGLGLG",
  "LGLGGL",
  "LGGLGL",
];

const CODE128_PATTERNS = [
  "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
  "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
  "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
  "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
  "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
  "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
  "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
  "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
  "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
  "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
  "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
];

interface ProductBarcodeImageProps {
  value?: string | null;
}

function toRuns(bits: string) {
  const runs: Array<{ black: boolean; width: number }> = [];
  for (const bit of bits) {
    const black = bit === "1";
    const last = runs[runs.length - 1];
    if (last && last.black === black) {
      last.width += 1;
    } else {
      runs.push({ black, width: 1 });
    }
  }
  return runs;
}

function encodeEAN13(value: string) {
  const digits = value.split("").map(Number);
  const parity = EAN13_PARITY[digits[0]];
  let bits = "101";

  for (let index = 1; index <= 6; index += 1) {
    bits += parity[index - 1] === "L" ? EAN13_LEFT_ODD[digits[index]] : EAN13_LEFT_EVEN[digits[index]];
  }

  bits += "01010";
  for (let index = 7; index <= 12; index += 1) {
    bits += EAN13_RIGHT[digits[index]];
  }

  return `${bits}101`;
}

function encodeCode128(value: string) {
  const chars = Array.from(value || " ");
  const codes = chars.map((char) => {
    const code = char.charCodeAt(0);
    return code >= 32 && code <= 126 ? code - 32 : 31;
  });

  const checksum = codes.reduce((total, code, index) => total + code * (index + 1), 104) % 103;
  const allCodes = [104, ...codes, checksum, 106];
  return allCodes.flatMap((code) => {
    const pattern = CODE128_PATTERNS[code] ?? CODE128_PATTERNS[0];
    return Array.from(pattern).map((width, index) => ({
      black: index % 2 === 0,
      width: Number(width),
    }));
  });
}

export function ProductBarcodeImage({ value }: ProductBarcodeImageProps) {
  const normalized = value?.trim();

  if (!normalized) {
    return null;
  }

  const format = resolveBarcodeFormat(normalized);
  const runs = format === "EAN13" ? toRuns(encodeEAN13(normalized)) : encodeCode128(normalized);

  return (
    <View style={styles.wrapper}>
      <View
        style={styles.bars}
        accessibilityLabel={normalized}
      >
        {runs.map((run, index) => (
          <View
            key={`${index}-${run.width}-${run.black ? "b" : "w"}`}
            style={[
              styles.bar,
              {
                flexGrow: run.width,
                backgroundColor: run.black ? "#111" : "#fff",
              },
            ]}
          />
        ))}
      </View>
      <Text variant="labelSmall" style={styles.format}>
        {normalized}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    gap: 2,
  },
  bars: {
    height: 28,
    width: "100%",
    overflow: "hidden",
    flexDirection: "row",
    borderRadius: 4,
    backgroundColor: "#fff",
    paddingHorizontal: 5,
    paddingVertical: 3,
  },
  bar: {
    height: "100%",
    flexBasis: 0,
  },
  format: {
    fontSize: 10,
    color: "#777",
    fontVariant: ["tabular-nums"],
    letterSpacing: 0.2,
    maxWidth: 124,
  },
});
