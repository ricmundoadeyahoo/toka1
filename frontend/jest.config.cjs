/** Jest + ts-jest + jsdom (React Testing Library) */
module.exports = {
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  transform: {
    '^.+\\.(ts|tsx)$': ['ts-jest', { tsconfig: '<rootDir>/tsconfig.jest.json' }]
  },
  moduleNameMapper: {
    '\\.(css)$': 'identity-obj-proxy',
    // axios publica ESM para navegador; en Jest usamos su build CommonJS
    '^axios$': 'axios/dist/node/axios.cjs'
  },
  setupFilesAfterEnv: ['<rootDir>/src/setupTests.ts'],
  collectCoverageFrom: ['src/**/*.{ts,tsx}', '!src/main.tsx', '!src/**/*.d.ts', '!src/setupTests.ts'],
  coverageReporters: ['text-summary', 'lcov', 'html']
};
