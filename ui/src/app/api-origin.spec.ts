import { apiOrigin } from './api-origin';
describe('API origin', () => {
  it('keeps development ports local', () => {
    expect(apiOrigin(5410, {hostname:'localhost',origin:'http://localhost:4300'})).toBe('http://127.0.0.1:5410');
  });
  it('uses the HTTPS public origin for every production mode', () => {
    for (const port of [5391,5393,5398,5410]) {
      expect(apiOrigin(port, {hostname:'3.149.236.79',origin:'https://3.149.236.79'})).toBe('https://3.149.236.79');
    }
  });
});
