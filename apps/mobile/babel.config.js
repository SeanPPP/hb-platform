module.exports = function (api) {
  api.cache(true);
  return {
    presets: ['babel-preset-expo'],
    plugins: [
      [
        'module-resolver',
        {
          root: ['./'],
          alias: {
            '@': './src',
            '@shared': './src/shared',
            '@modules': './src/modules',
            '@components': './src/components',
            '@store': './src/store',
            '@assets': './assets',
          },
        },
      ],
    ],
  };
};
