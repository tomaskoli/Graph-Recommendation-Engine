/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { SearchBrandResultDto } from './SearchBrandResultDto';
import type { SearchCategoryResultDto } from './SearchCategoryResultDto';
import type { SearchProductResultDto } from './SearchProductResultDto';
export type SearchResultDto = {
    products: Array<SearchProductResultDto>;
    categories: Array<SearchCategoryResultDto>;
    brands: Array<SearchBrandResultDto>;
};

