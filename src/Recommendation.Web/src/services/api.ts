import { OpenAPI } from '../api/core/OpenAPI';
import {
  ProductsService,
  CategoriesService,
  RecommendationsService,
  SegmentsService,
  SearchService,
} from '../api';

// Configure API base URL from environment variable
// Empty string means relative URLs (for Docker/nginx proxy)
OpenAPI.BASE = import.meta.env.VITE_API_URL ?? '';

// Re-export all types and services from generated client
export type {
  ProductDto,
  ProductDetailDto,
  ParameterDto,
  CategoryDto,
  RecommendationDto,
  ScoredProductDto,
  CatalogSegmentDto,
  PaginatedResultOfProductDto,
  PaginatedResultOfCategoryDto,
  PaginatedResultOfScoredProductDto,
  PaginatedResultOfCatalogSegmentDto,
  SearchResultDto,
  SearchProductResultDto,
  SearchCategoryResultDto,
  SearchBrandResultDto,
} from '../api';

export {
  ProductsService,
  CategoriesService,
  RecommendationsService,
  SegmentsService,
  SearchService,
  ApiError,
} from '../api';

// Facade for backward compatibility - extracts items from paginated results
export const api = {
  getProduct: (id: number) =>
    ProductsService.getProductById({ id }),

  getRelatedProducts: async (id: number, page = 1, pageSize = 10) => {
    const result = await ProductsService.getRelatedProducts({ id, page, pageSize });
    return result.items;
  },

  getCategories: async (page = 1, pageSize = 100) => {
    const result = await CategoriesService.getCategoryHierarchy({ page, pageSize });
    return result.items;
  },

  getCategoryById: async (id: number) => {
    const result = await CategoriesService.getCategoryById({ id });
    return result.items;
  },

  getProductsByCategory: async (id: number, page = 1, pageSize = 20) => {
    const result = await CategoriesService.getProductsByCategory({ id, page, pageSize });
    return result.items;
  },

  getRecommendations: (productId: number, page = 1, pageSize = 50) =>
    RecommendationsService.getRecommendations({ productId, page, pageSize }),

  getSegments: async (page = 1, pageSize = 20) => {
    const result = await SegmentsService.getAllSegments({ page, pageSize });
    return result.items;
  },

  getCategoriesBySegment: async (id: number, page = 1, pageSize = 20) => {
    const result = await SegmentsService.getCategoriesBySegment({ id, page, pageSize });
    return result.items;
  },

  search: (q: string, limit = 5) =>
    SearchService.globalSearch({ q, limit }),
};
