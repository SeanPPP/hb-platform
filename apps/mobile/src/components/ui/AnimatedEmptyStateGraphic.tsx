import { useEffect, useRef } from "react";
import { Animated, Easing, StyleSheet } from "react-native";
import Svg, { Circle, G, Line, Path, Rect } from "react-native-svg";

export function AnimatedEmptyStateGraphic({ size = 112 }: { size?: number }) {
  const floatValue = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    // 只动画外层容器，避免 SVG 路径在每一帧频繁重绘。
    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(floatValue, {
          toValue: 1,
          duration: 1400,
          easing: Easing.inOut(Easing.quad),
          useNativeDriver: true,
        }),
        Animated.timing(floatValue, {
          toValue: 0,
          duration: 1400,
          easing: Easing.inOut(Easing.quad),
          useNativeDriver: true,
        }),
      ])
    );

    loop.start();

    return () => {
      loop.stop();
    };
  }, [floatValue]);

  const translateY = floatValue.interpolate({
    inputRange: [0, 1],
    outputRange: [0, -6],
  });
  const opacity = floatValue.interpolate({
    inputRange: [0, 1],
    outputRange: [0.88, 1],
  });

  return (
    <Animated.View
      pointerEvents="none"
      style={[
        styles.container,
        {
          width: size,
          height: size,
          opacity,
          transform: [{ translateY }],
        },
      ]}
    >
      <Svg width={size} height={size} viewBox="0 0 112 112" fill="none">
        <Circle cx="56" cy="56" r="50" fill="#EAF4FF" />
        <Circle cx="86" cy="24" r="7" fill="#C8E6FF" />
        <Circle cx="23" cy="35" r="5" fill="#D9F7E7" />
        <Path d="M30 45L56 31L82 45V76L56 91L30 76V45Z" fill="#FFFFFF" stroke="#8FC7F4" strokeWidth="3" />
        <Path d="M30 45L56 60L82 45" stroke="#8FC7F4" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
        <Path d="M56 60V90" stroke="#8FC7F4" strokeWidth="3" strokeLinecap="round" />
        <G opacity="0.95">
          <Rect x="42" y="52" width="28" height="18" rx="5" fill="#F8FBFF" stroke="#174A7C" strokeWidth="2" />
          <Line x1="48" y1="57" x2="48" y2="65" stroke="#174A7C" strokeWidth="2" strokeLinecap="round" />
          <Line x1="53" y1="57" x2="53" y2="65" stroke="#174A7C" strokeWidth="1.5" strokeLinecap="round" />
          <Line x1="59" y1="57" x2="59" y2="65" stroke="#174A7C" strokeWidth="2" strokeLinecap="round" />
          <Line x1="64" y1="57" x2="64" y2="65" stroke="#174A7C" strokeWidth="1.5" strokeLinecap="round" />
        </G>
        <Path d="M76 34H86V44" stroke="#0B704F" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
        <Path d="M86 34L73 47" stroke="#0B704F" strokeWidth="3" strokeLinecap="round" />
        <Path d="M25 79C31 86 39 89 49 88" stroke="#74C69D" strokeWidth="3" strokeLinecap="round" />
      </Svg>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  container: {
    alignItems: "center",
    justifyContent: "center",
  },
});
